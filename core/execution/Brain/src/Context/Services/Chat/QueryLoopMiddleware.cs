using JoinCode.Abstractions.Diagnostics;

namespace Core.Context;

/// <summary>
/// 查询循环中间件 — LLM 调用 + 块处理 + 工具执行循环
/// 对应原 ChatService.StreamWithEventsAsync 中的 while 循环
/// 职责已拆分到: BackgroundNotificationHandler / LLMInvocationHandler / ToolExecutionHandler / InformationEntropyGuardian / TelemetryRecorder
/// 支持流式工具执行：对齐 TS StreamingToolExecutor，流式期间收到 tool_use block 立即执行
/// </summary>
[Register]
public sealed partial class QueryLoopMiddleware : ServiceEntity, IChatMiddleware {
    private const int MaxToolCallIterations = 128;

    private readonly IBackgroundNotificationHandler _notificationHandler;
    private readonly ILLMInvocationHandler _llmHandler;
    private readonly IToolExecutionHandler _toolHandler;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly IChatContextManager _contextManager;
    private readonly IEmptyResponseTracker _emptyResponseTracker;
    private readonly QueryLoopServices? _services;
    private readonly ILoopDetectionStrategy _loopDetectionStrategy;
    private readonly IToolConcurrencyClassifier? _concurrencyClassifier;
    private readonly ToolExecutionSettings? _toolExecutionSettings;
    [Inject] private readonly ILogger<QueryLoopMiddleware>? _logger;

    public QueryLoopMiddleware(
        IBackgroundNotificationHandler notificationHandler,
        ILLMInvocationHandler llmHandler,
        IToolExecutionHandler toolHandler,
        ITelemetryRecorder telemetryRecorder,
        IChatContextManager contextManager,
        IEmptyResponseTracker emptyResponseTracker,
        QueryLoopServices? services = null,
        ILoopDetectionStrategy? loopDetectionStrategy = null,
        IToolConcurrencyClassifier? concurrencyClassifier = null,
        ToolExecutionSettings? toolExecutionSettings = null,
        ILogger<QueryLoopMiddleware>? logger = null) {
        _notificationHandler = notificationHandler;
        _llmHandler = llmHandler;
        _toolHandler = toolHandler;
        _telemetryRecorder = telemetryRecorder;
        _contextManager = contextManager;
        _emptyResponseTracker = emptyResponseTracker;
        _services = services;
        _concurrencyClassifier = concurrencyClassifier;
        _toolExecutionSettings = toolExecutionSettings;
        _logger = logger;
        _loopDetectionStrategy = loopDetectionStrategy ?? new InformationEntropyGuardian(logger: logger);
    }

    private bool UseStreamingToolExecution =>
        _concurrencyClassifier is not null && (_toolExecutionSettings?.UseStreamingToolExecution ?? false);

    private int MaxConcurrency => _toolExecutionSettings?.MaxParallelToolExecution ?? 10;

    /// <summary>
    /// 处理聊天事件流：while 循环执行 LLM 调用和工具执行
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct) {
        var totalToolCalls = 0;
        TokenUsage? finalUsage = null;
        string? finalModelId = null;

#if DEBUG
        if (System.Diagnostics.Debugger.IsAttached) System.Diagnostics.Debugger.Break();
#endif

        while (totalToolCalls < MaxToolCallIterations) {
            ct.ThrowIfCancellationRequested();

            await _notificationHandler.ProcessPendingNotificationsAsync(ct).ConfigureAwait(false);

            var historySnapshot = await _contextManager.GetMessageListAsync(ct).ConfigureAwait(false);
            Diag.WriteLine($"[LOOP] 迭代 #{totalToolCalls} | 消息={historySnapshot.Count}");
#if DEBUG
            if (System.Diagnostics.Debugger.IsAttached) System.Diagnostics.Debugger.Break();
#endif
            await _telemetryRecorder.RecordTurnTelemetryAsync(historySnapshot, totalToolCalls).ConfigureAwait(false);

            var iterState = new IterationState();

            if (_loopDetectionStrategy is InformationEntropyGuardian guardian)
                guardian.SetContext(context.SpanName, context.ConversationTurn, totalToolCalls);

            if (UseStreamingToolExecution) {
                await foreach (var evt in ProcessStreamingModeAsync(historySnapshot, context, iterState, totalToolCalls, ct).ConfigureAwait(false)) {
                    yield return evt;
                }
            } else {
                await foreach (var evt in ProcessTraditionalModeAsync(historySnapshot, context, iterState, totalToolCalls, ct).ConfigureAwait(false)) {
                    yield return evt;
                }
            }

            if (iterState.StreamUsage is not null) finalUsage = iterState.StreamUsage;
            if (iterState.StreamModelId is not null) finalModelId = iterState.StreamModelId;

            if (iterState.ToolCallName is not null) {
                totalToolCalls += iterState.ToolCalls.Count;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(iterState.FullResponse.ToString())) {
                _emptyResponseTracker.Reset();
                break;
            }

            if (totalToolCalls > 0) {
                var exceeded = _emptyResponseTracker.RecordEmptyResponse();
                if (exceeded) {
                    Diag.WriteLine($"[LOOP {iterState.CallId}] 工具调用后空白响应已达{_emptyResponseTracker.MaxConsecutiveEmpty}次, 结束本轮对话");
                    yield return ChatStreamEvent.Text($"⚠ 模型在工具调用后连续{_emptyResponseTracker.MaxConsecutiveEmpty}次返回空白响应，本轮对话已结束。");
                    break;
                }
                Diag.WriteLine($"[LOOP {iterState.CallId}] 工具调用后空白响应({_emptyResponseTracker.ConsecutiveEmptyCount}/{_emptyResponseTracker.MaxConsecutiveEmpty}), 注入系统提示词让AI继续");
                if (!context.IsDryRun)
                    await _contextManager.AddSystemMessageAsync(
                        _emptyResponseTracker.BuildInterventionPrompt(),
                        ct).ConfigureAwait(false);
                continue;
            }

            Diag.WriteLine($"[LOOP {iterState.CallId}] LLM 空响应, 结束本轮对话");
            yield return ChatStreamEvent.Text("⚠ 模型返回了空白响应，本轮对话已结束。");
            break;
        }

        if (totalToolCalls >= MaxToolCallIterations) {
            Diag.WriteLine($"[LOOP] 达到最大工具调用次数限制: {MaxToolCallIterations}");
            yield return ChatStreamEvent.Text(
                $"⚠ 已达到最大工具调用次数（{MaxToolCallIterations} 次），为避免死循环本轮对话已被截断。");
        }

        context.TotalToolCalls = totalToolCalls;
        context.FinalUsage = finalUsage;
        context.FinalModelId = finalModelId;

        _services?.IdleDetector?.RecordAssistantTurn(null);
        await _contextManager.SyncDiscoveredToolsFromHistoryAsync(ct).ConfigureAwait(false);

        if (_services?.PostSamplingCallbacks is not null) {
            var sessionId = (_contextManager is ChatContextManager cm) ? cm.SessionId : null;
            var postSamplingCtx = new PostSamplingContext {
                EstimatedTokenCount = finalUsage?.TotalTokens ?? 0,
                ToolCallsSinceLastExtraction = totalToolCalls,
                QuerySource = "repl_main_thread",
                SessionId = sessionId,
                CancellationToken = ct
            };
            await _services.PostSamplingCallbacks.FireAsync(postSamplingCtx).ConfigureAwait(false);
        }

        await foreach (var evt in next(context, ct).ConfigureAwait(false)) {
            yield return evt;
        }

        yield return ChatStreamEvent.Done(finalUsage, finalModelId);
    }

    /// <summary>
    /// 流式工具执行模式 — 对齐 TS StreamingToolExecutor
    /// </summary>
    private async IAsyncEnumerable<ChatStreamEvent> ProcessStreamingModeAsync(
        MessageList historySnapshot, ChatMiddlewareContext context, IterationState iterState,
        int totalToolCalls, [EnumeratorCancellation] CancellationToken ct) {

        var streamingExecutor = new StreamingToolExecutor(
            _toolHandler, _concurrencyClassifier!, context, MaxConcurrency, _logger, ct);

        await foreach (var evt in _llmHandler.InvokeLLMAsync(
            historySnapshot, context.ExecutionSettings, context, totalToolCalls, iterState,
            streamingToolExecution: true, ct: ct).ConfigureAwait(false)) {
            yield return evt;
        }

        if (iterState.ToolCallName is null) {
            Diag.WriteLine($"[LOOP {iterState.CallId}] 纯文本响应, 长度={iterState.FullResponse.Length}");
            if (iterState.FullResponse.Length > 0) {
                var (pureEvents, pureResponse) = BuildPureTextResponse(iterState, context);
                foreach (var evt in pureEvents)
                    yield return evt;
                if (!context.IsDryRun && !string.IsNullOrWhiteSpace(pureResponse))
                    await _contextManager.AddAssistantMessageAsync(pureResponse, ct).ConfigureAwait(false);
            }
            yield break;
        }

        var toolCalls = _toolHandler.PrepareToolCalls(iterState);
        var assistantContent = iterState.FullResponse.Length > 0 ? iterState.FullResponse.ToString() : null;
        var assistantMetadata = ToolCallEntry.BuildAssistantMetadata(toolCalls);
        await _contextManager.AddAssistantToolCallMessageAsync(assistantContent, assistantMetadata, ct).ConfigureAwait(false);

        for (var idx = 0; idx < toolCalls.Count; idx++) {
            var toolCall = toolCalls[idx];
            var currentArgs = JsonArgumentParser.Parse(toolCall.Arguments);
            var toolLoop = _loopDetectionStrategy.CheckToolCallLoop(toolCall.Name, currentArgs);
            if (toolLoop is not null) {
                yield return ChatStreamEvent.LoopDetected(toolLoop.TriggerCount, toolLoop.ToolCallCount, toolLoop.Reason);
            }

            yield return ChatStreamEvent.ToolStart(toolCall.Name, toolCall.Id, toolCall.Arguments);
            await streamingExecutor.AddToolAsync(toolCall, idx).ConfigureAwait(false);
        }

        IReadOnlyList<StreamingToolResult> allResults;
        try {
            allResults = await streamingExecutor.GetRemainingResultsAsync().ConfigureAwait(false);
        } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
            await _toolHandler.WriteAbortedToolResultsAsync(toolCalls, 0, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        foreach (var result in allResults) {
            Diag.WriteLine($"[TOOL {iterState.CallId}] {result.ToolName} → {(result.Result.IsError ? "ERROR" : "OK")} | 长度={result.Result.ResultText?.Length ?? 0}");

            yield return result.ToToolEndEvent();

            try {
                await _toolHandler.ApplyToolResultToContextAsync(
                    result.ToolName, result.ToolCallId, result.Result.ResultText,
                    result.Result.IsError, result.Result.ContentBlocks, context, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                await _toolHandler.WriteAbortedToolResultsAsync(toolCalls, 0, CancellationToken.None).ConfigureAwait(false);
                throw;
            } catch (Exception applyEx) {
                _logger?.LogError(applyEx, "[TOOL {CallId}] 应用结果到上下文失败: {ToolName}", iterState.CallId, result.ToolName);
                try {
                    var placeholderMetadata = ToolCallEntry.BuildToolResultMetadata(result.ToolCallId, result.ToolName);
                    await _contextManager.AddToolResultMessageAsync(
                        $"(工具结果应用失败: {applyEx.Message})", placeholderMetadata, null, CancellationToken.None)
                        .ConfigureAwait(false);
                } catch (Exception placeholderEx) {
                    _logger?.LogError(placeholderEx, "[TOOL {CallId}] 写入占位结果也失败，中断回合", iterState.CallId);
                    throw;
                }
            }
        }
    }

    /// <summary>
    /// 传统模式 — 流式结束后顺序执行工具
    /// </summary>
    private async IAsyncEnumerable<ChatStreamEvent> ProcessTraditionalModeAsync(
        MessageList historySnapshot, ChatMiddlewareContext context, IterationState iterState,
        int totalToolCalls, [EnumeratorCancellation] CancellationToken ct) {

        await foreach (var evt in _llmHandler.InvokeLLMAsync(
            historySnapshot, context.ExecutionSettings, context, totalToolCalls, iterState, ct: ct)
            .ConfigureAwait(false)) {
            yield return evt;
        }

        if (iterState.ToolCallName is null) {
            Diag.WriteLine($"[LOOP {iterState.CallId}] 纯文本响应, 长度={iterState.FullResponse.Length}");
            if (iterState.FullResponse.Length > 0) {
                var (pureEvents, pureResponse) = BuildPureTextResponse(iterState, context);
                foreach (var evt in pureEvents)
                    yield return evt;
                if (!context.IsDryRun && !string.IsNullOrWhiteSpace(pureResponse))
                    await _contextManager.AddAssistantMessageAsync(pureResponse, ct).ConfigureAwait(false);
            }
            yield break;
        }

        var toolCalls = _toolHandler.PrepareToolCalls(iterState);
        var assistantContent = iterState.FullResponse.Length > 0 ? iterState.FullResponse.ToString() : null;
        var assistantMetadata = ToolCallEntry.BuildAssistantMetadata(toolCalls);
        await _contextManager.AddAssistantToolCallMessageAsync(assistantContent, assistantMetadata, ct).ConfigureAwait(false);

        for (var idx = 0; idx < toolCalls.Count; idx++) {
            var toolCall = toolCalls[idx];
            var currentArgs = JsonArgumentParser.Parse(toolCall.Arguments);
            var toolLoop = _loopDetectionStrategy.CheckToolCallLoop(toolCall.Name, currentArgs);
            if (toolLoop is not null) {
                yield return ChatStreamEvent.LoopDetected(toolLoop.TriggerCount, toolLoop.ToolCallCount, toolLoop.Reason);
            }

            yield return ChatStreamEvent.ToolStart(toolCall.Name, toolCall.Id, toolCall.Arguments);

            ToolCallResult toolCallResult;
            try {
                toolCallResult = await _toolHandler.ExecuteToolCallAsync(
                    toolCall.Name, toolCall.Id, currentArgs, context, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                await _toolHandler.WriteAbortedToolResultsAsync(toolCalls, idx, CancellationToken.None).ConfigureAwait(false);
                throw;
            }

            Diag.WriteLine($"[TOOL {iterState.CallId}] {toolCall.Name} → {(toolCallResult.IsError ? "ERROR" : "OK")}");

            yield return ChatStreamEvent.ToolEnd(
                toolCall.Name, toolCallResult.ResultText, toolCall.Id,
                toolCallResult.IsError, toolCallResult.StructuredPatch);

            try {
                await _toolHandler.ApplyToolResultToContextAsync(
                    toolCall.Name, toolCall.Id, toolCallResult.ResultText,
                    toolCallResult.IsError, toolCallResult.ContentBlocks, context, ct).ConfigureAwait(false);
            } catch (OperationCanceledException) when (ct.IsCancellationRequested) {
                await _toolHandler.WriteAbortedToolResultsAsync(toolCalls, idx, CancellationToken.None).ConfigureAwait(false);
                throw;
            } catch (Exception applyEx) {
                _logger?.LogError(applyEx, "[TOOL {CallId}] 应用结果到上下文失败: {ToolName}", iterState.CallId, toolCall.Name);
                try {
                    var placeholderMetadata = ToolCallEntry.BuildToolResultMetadata(toolCall.Id, toolCall.Name);
                    await _contextManager.AddToolResultMessageAsync(
                        $"(工具结果应用失败: {applyEx.Message})", placeholderMetadata, null, CancellationToken.None)
                        .ConfigureAwait(false);
                } catch (Exception placeholderEx) {
                    _logger?.LogError(placeholderEx, "[TOOL {CallId}] 写入占位结果也失败，中断回合", iterState.CallId);
                    throw;
                }
            }
        }
    }

    private (List<ChatStreamEvent> Events, string FinalResponse) BuildPureTextResponse(
        IterationState iterState, ChatMiddlewareContext context) {
        var events = new List<ChatStreamEvent>();
        var aiResponse = iterState.FullResponse.ToString();

        var textLoop = _loopDetectionStrategy.CheckTextLoop(aiResponse);
        if (textLoop is not null) {
            Diag.WriteLine("[LOOP] 逻辑指纹循环已触发");
            events.Add(ChatStreamEvent.LoopDetected(textLoop.TriggerCount, textLoop.ToolCallCount, textLoop.Reason));
        }

        return (events, aiResponse);
    }
}
