namespace Core.Context;

/// <summary>
/// 查询循环中间件 — LLM 调用 + 块处理 + 工具执行循环
/// 对应原 ChatService.StreamWithEventsAsync 中的 while 循环
/// 职责已拆分到: BackgroundNotificationHandler / LLMInvocationHandler / ToolExecutionHandler / CompositeLoopDetectionStrategy / TelemetryRecorder
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
    [Inject] private readonly ILogger<QueryLoopMiddleware>? _logger;

    public QueryLoopMiddleware(
        IBackgroundNotificationHandler notificationHandler,
        ILLMInvocationHandler llmHandler,
        IToolExecutionHandler toolHandler,
        ITelemetryRecorder telemetryRecorder,
        IChatContextManager contextManager,
        QueryLoopServices? services = null,
        ILoopDetectionStrategy? loopDetectionStrategy = null,
        ILogger<QueryLoopMiddleware>? logger = null)
    {
        _notificationHandler = notificationHandler;
        _llmHandler = llmHandler;
        _toolHandler = toolHandler;
        _telemetryRecorder = telemetryRecorder;
        _contextManager = contextManager;
        _services = services;
        _logger = logger;
        _loopDetectionStrategy = loopDetectionStrategy ?? new CompositeLoopDetectionStrategy(logger);
    }

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

            // 1. 处理后台代理通知
            await _notificationHandler.ProcessPendingNotificationsAsync(ct).ConfigureAwait(false);

            // 2. 获取历史快照 + 遥测
            var historySnapshot = await _contextManager.GetMessageListAsync(ct).ConfigureAwait(false);
            _telemetryRecorder.RecordTurnTelemetry(historySnapshot, totalToolCalls);

            // 3. 调用 LLM（调用方创建迭代状态，处理器在流式过程中填充）
            var iterState = new IterationState();
            await foreach (var evt in _llmHandler.InvokeLLMAsync(
                historySnapshot, context.ExecutionSettings, context, totalToolCalls, iterState, ct)
                .ConfigureAwait(false))
            {
                yield return evt;
            }

            // 4. 更新用量信息
            if (iterState.StreamUsage is not null) finalUsage = iterState.StreamUsage;
            if (iterState.StreamModelId is not null) finalModelId = iterState.StreamModelId;

            // 5. 纯文本响应 — 循环检测后结束
            if (iterState.ToolCallName is null)
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
                    await _contextManager.AddAssistantMessageAsync(aiResponse, ct).ConfigureAwait(false);
                break;
            }

            // 6. 工具调用 — 准备调用列表
            var toolCalls = _toolHandler.PrepareToolCalls(iterState);

            // 构建包含全部工具调用的 assistant 消息（一次性添加到历史）
            var assistantContent = iterState.FullResponse.Length > 0 ? iterState.FullResponse.ToString() : null;
            var assistantMetadata = ToolCallEntry.BuildAssistantMetadata(toolCalls);
            await _contextManager.AddAssistantToolCallMessageAsync(assistantContent, assistantMetadata, ct).ConfigureAwait(false);

            // 7. 顺序执行每个工具调用
            for (var idx = 0; idx < toolCalls.Count; idx++)
            {
                var toolCall = toolCalls[idx];
                totalToolCalls++;

                // 解析当前工具调用的参数
                var currentArgs = JsonArgumentParser.Parse(toolCall.Arguments);

                // 循环检测
                var toolLoop = _loopDetectionStrategy.CheckToolCallLoop(toolCall.Name, currentArgs);
                if (toolLoop is not null)
                {
                    yield return ChatStreamEvent.LoopDetected(toolLoop.TriggerCount, toolLoop.ToolCallCount, toolLoop.Reason);
                }

                yield return ChatStreamEvent.ToolStart(toolCall.Name, toolCall.Id, toolCall.Arguments);

                // 执行工具调用（含 ContextModifier 和消息注入）
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

                // 持久化工具结果到上下文
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

        if (totalToolCalls >= MaxToolCallIterations)
        {
            _logger?.LogWarning("[QueryLoopMiddleware] 达到最大工具调用次数限制: {Max}", MaxToolCallIterations);
        }

        // 写入上下文供下游中间件使用
        context.TotalToolCalls = totalToolCalls;
        context.FinalUsage = finalUsage;
        context.FinalModelId = finalModelId;

        // 空闲检测
        _services?.IdleDetector?.RecordAssistantTurn(null);
        await _contextManager.SyncDiscoveredToolsFromHistoryAsync(ct).ConfigureAwait(false);

        // Post-sampling 回调（SessionMemory 提取等）
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

        // 调用下游（PostChatMiddleware）
        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }

        // Done 事件在所有中间件之后发射
        yield return ChatStreamEvent.Done(finalUsage, finalModelId);
    }
}
