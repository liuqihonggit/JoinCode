namespace Core.Context;

/// <summary>
/// 查询循环中间件 — LLM 调用 + 块处理 + 工具执行循环
/// 对应原 ChatService.StreamWithEventsAsync 中的 while 循环
/// 职责已拆分到: BackgroundNotificationHandler / LLMInvocationHandler / ToolExecutionHandler / CompositeLoopDetectionStrategy / TelemetryRecorder
/// 支持流式工具执行：对齐 TS StreamingToolExecutor，流式期间收到 tool_use block 立即执行
/// </summary>
[Register]
public sealed partial class QueryLoopMiddleware : IChatMiddleware
{
    private const int MaxToolCallIterations = 128;

    private readonly IBackgroundNotificationHandler _notificationHandler;
    private readonly ILLMInvocationHandler _llmHandler;
    private readonly IToolExecutionHandler _toolHandler;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly IChatContextManager _contextManager;
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
        QueryLoopServices? services = null,
        ILoopDetectionStrategy? loopDetectionStrategy = null,
        IToolConcurrencyClassifier? concurrencyClassifier = null,
        ToolExecutionSettings? toolExecutionSettings = null,
        ILogger<QueryLoopMiddleware>? logger = null)
    {
        _notificationHandler = notificationHandler;
        _llmHandler = llmHandler;
        _toolHandler = toolHandler;
        _telemetryRecorder = telemetryRecorder;
        _contextManager = contextManager;
        _services = services;
        _concurrencyClassifier = concurrencyClassifier;
        _toolExecutionSettings = toolExecutionSettings;
        _logger = logger;
        _loopDetectionStrategy = loopDetectionStrategy ?? new CompositeLoopDetectionStrategy(logger);
    }

    /// <summary>
    /// 是否启用流式工具执行 — 需要 IToolConcurrencyClassifier 已注册且配置开关已启用
    /// </summary>
    private bool UseStreamingToolExecution =>
        _concurrencyClassifier is not null && (_toolExecutionSettings?.UseStreamingToolExecution ?? false);

    /// <summary>
    /// 最大并发数 — 对齐 TS CLAUDE_CODE_MAX_TOOL_USE_CONCURRENCY
    /// </summary>
    private int MaxConcurrency => _toolExecutionSettings?.MaxParallelToolExecution ?? 10;

    /// <summary>
    /// 处理聊天事件流：while 循环执行 LLM 调用和工具执行
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var totalToolCalls = 0;
        TokenUsage? finalUsage = null;
        string? finalModelId = null;

        while (totalToolCalls < MaxToolCallIterations)
        {
            ct.ThrowIfCancellationRequested();

            await _notificationHandler.ProcessPendingNotificationsAsync(ct).ConfigureAwait(false);

            var historySnapshot = await _contextManager.GetMessageListAsync(ct).ConfigureAwait(false);
            await _telemetryRecorder.RecordTurnTelemetryAsync(historySnapshot, totalToolCalls).ConfigureAwait(false);

            var iterState = new IterationState();

            if (UseStreamingToolExecution)
            {
                // 流式工具执行模式 — 对齐 TS StreamingToolExecutor
                var streamingExecutor = new StreamingToolExecutor(
                    _toolHandler, _concurrencyClassifier!, context, MaxConcurrency, _logger, ct);

                await foreach (var evt in _llmHandler.InvokeLLMAsync(
                    historySnapshot, context.ExecutionSettings, context, totalToolCalls, iterState,
                    streamingToolExecution: true, ct: ct).ConfigureAwait(false))
                {
                    yield return evt;
                }

                if (iterState.ToolCallName is null)
                {
                    foreach (var evt in HandlePureTextResponse(iterState, context))
                        yield return evt;
                    break;
                }

                var toolCalls = _toolHandler.PrepareToolCalls(iterState);
                var assistantContent = iterState.FullResponse.Length > 0 ? iterState.FullResponse.ToString() : null;
                var assistantMetadata = ToolCallEntry.BuildAssistantMetadata(toolCalls);
                await _contextManager.AddAssistantToolCallMessageAsync(assistantContent, assistantMetadata, ct).ConfigureAwait(false);

                // 将所有工具调用加入流式执行器
                for (var idx = 0; idx < toolCalls.Count; idx++)
                {
                    var toolCall = toolCalls[idx];
                    var currentArgs = JsonArgumentParser.Parse(toolCall.Arguments);
                    var toolLoop = _loopDetectionStrategy.CheckToolCallLoop(toolCall.Name, currentArgs);
                    if (toolLoop is not null)
                    {
                        yield return ChatStreamEvent.LoopDetected(toolLoop.TriggerCount, toolLoop.ToolCallCount, toolLoop.Reason);
                    }

                    yield return ChatStreamEvent.ToolStart(toolCall.Name, toolCall.Id, toolCall.Arguments);
                    await streamingExecutor.AddToolAsync(toolCall, idx).ConfigureAwait(false);
                    totalToolCalls++;
                }

                // 等待所有工具完成并输出结果
                IReadOnlyList<StreamingToolResult> allResults;
                try
                {
                    allResults = await streamingExecutor.GetRemainingResultsAsync().ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    await _toolHandler.WriteAbortedToolResultsAsync(toolCalls, 0, CancellationToken.None).ConfigureAwait(false);
                    throw;
                }

                foreach (var result in allResults)
                {
                    _logger?.LogInformation("[QueryLoopMiddleware] 工具调用: {ToolName} → {Result}",
                        result.ToolName, result.Result.IsError ? "ERROR" : "OK");

                    yield return result.ToToolEndEvent();

                    try
                    {
                        await _toolHandler.ApplyToolResultToContextAsync(
                            result.ToolName, result.ToolCallId, result.Result.ResultText,
                            result.Result.IsError, result.Result.ContentBlocks, context, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        await _toolHandler.WriteAbortedToolResultsAsync(toolCalls, 0, CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }
                }

                if (iterState.StreamUsage is not null) finalUsage = iterState.StreamUsage;
                if (iterState.StreamModelId is not null) finalModelId = iterState.StreamModelId;
            }
            else
            {
                // 传统模式 — 流式结束后顺序执行
                await foreach (var evt in _llmHandler.InvokeLLMAsync(
                    historySnapshot, context.ExecutionSettings, context, totalToolCalls, iterState, ct: ct)
                    .ConfigureAwait(false))
                {
                    yield return evt;
                }

                if (iterState.StreamUsage is not null) finalUsage = iterState.StreamUsage;
                if (iterState.StreamModelId is not null) finalModelId = iterState.StreamModelId;

                if (iterState.ToolCallName is null)
                {
                    foreach (var evt in HandlePureTextResponse(iterState, context))
                        yield return evt;
                    break;
                }

                var toolCalls = _toolHandler.PrepareToolCalls(iterState);
                var assistantContent = iterState.FullResponse.Length > 0 ? iterState.FullResponse.ToString() : null;
                var assistantMetadata = ToolCallEntry.BuildAssistantMetadata(toolCalls);
                await _contextManager.AddAssistantToolCallMessageAsync(assistantContent, assistantMetadata, ct).ConfigureAwait(false);

                for (var idx = 0; idx < toolCalls.Count; idx++)
                {
                    var toolCall = toolCalls[idx];
                    totalToolCalls++;

                    var currentArgs = JsonArgumentParser.Parse(toolCall.Arguments);
                    var toolLoop = _loopDetectionStrategy.CheckToolCallLoop(toolCall.Name, currentArgs);
                    if (toolLoop is not null)
                    {
                        yield return ChatStreamEvent.LoopDetected(toolLoop.TriggerCount, toolLoop.ToolCallCount, toolLoop.Reason);
                    }

                    yield return ChatStreamEvent.ToolStart(toolCall.Name, toolCall.Id, toolCall.Arguments);

                    ToolCallResult toolCallResult;
                    try
                    {
                        toolCallResult = await _toolHandler.ExecuteToolCallAsync(
                            toolCall.Name, toolCall.Id, currentArgs, context, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        await _toolHandler.WriteAbortedToolResultsAsync(toolCalls, idx, CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }

                    _logger?.LogInformation("[QueryLoopMiddleware] 工具调用 #{Num}: {ToolName} → {Result}",
                        totalToolCalls, toolCall.Name, toolCallResult.IsError ? "ERROR" : "OK");

                    yield return ChatStreamEvent.ToolEnd(
                        toolCall.Name, toolCallResult.ResultText, toolCall.Id,
                        toolCallResult.IsError, toolCallResult.StructuredPatch);

                    try
                    {
                        await _toolHandler.ApplyToolResultToContextAsync(
                            toolCall.Name, toolCall.Id, toolCallResult.ResultText,
                            toolCallResult.IsError, toolCallResult.ContentBlocks, context, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        await _toolHandler.WriteAbortedToolResultsAsync(toolCalls, idx, CancellationToken.None).ConfigureAwait(false);
                        throw;
                    }
                }
            }

            if (iterState.StreamUsage is not null) finalUsage = iterState.StreamUsage;
            if (iterState.StreamModelId is not null) finalModelId = iterState.StreamModelId;
        }

        if (totalToolCalls >= MaxToolCallIterations)
        {
            _logger?.LogWarning("[QueryLoopMiddleware] 达到最大工具调用次数限制: {Max}", MaxToolCallIterations);
        }

        context.TotalToolCalls = totalToolCalls;
        context.FinalUsage = finalUsage;
        context.FinalModelId = finalModelId;

        _services?.IdleDetector?.RecordAssistantTurn(null);
        await _contextManager.SyncDiscoveredToolsFromHistoryAsync(ct).ConfigureAwait(false);

        if (_services?.PostSamplingCallbacks is not null)
        {
            var sessionId = (_contextManager is ChatContextManager cm) ? cm.SessionId : null;
            var postSamplingCtx = new PostSamplingContext
            {
                EstimatedTokenCount = finalUsage?.TotalTokens ?? 0,
                ToolCallsSinceLastExtraction = totalToolCalls,
                QuerySource = "repl_main_thread",
                SessionId = sessionId,
                CancellationToken = ct
            };
            await _services.PostSamplingCallbacks.FireAsync(postSamplingCtx).ConfigureAwait(false);
        }

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }

        yield return ChatStreamEvent.Done(finalUsage, finalModelId);
    }

    /// <summary>
    /// 处理纯文本响应 — 循环检测后结束
    /// </summary>
    private IEnumerable<ChatStreamEvent> HandlePureTextResponse(
        IterationState iterState, ChatMiddlewareContext context)
    {
        var aiResponse = iterState.FullResponse.ToString();
        if (string.IsNullOrEmpty(aiResponse))
        {
            aiResponse = "抱歉，我无法生成回复。";
            yield return ChatStreamEvent.Text(aiResponse);
        }

        var textLoop = _loopDetectionStrategy.CheckTextLoop(aiResponse);
        if (textLoop is not null)
        {
            _logger?.LogWarning("[QueryLoopMiddleware] 逻辑指纹循环已触发");
            yield return ChatStreamEvent.LoopDetected(textLoop.TriggerCount, textLoop.ToolCallCount, textLoop.Reason);
        }

        if (!context.IsDryRun)
            _contextManager.AddAssistantMessageAsync(aiResponse, default).GetAwaiter().GetResult();
    }
}
