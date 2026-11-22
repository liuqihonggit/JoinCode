namespace Core.Context;

/// <summary>
/// 聊天循环中间件 — ChatService 与 QueryEngine 的桥接（TASK103 合并后唯一循环实现）：
/// 把整轮 ReAct 循环委托给 <see cref="IQueryEngine"/>，输出经适配器转为聊天事件，
/// 期间合流子代理事件与轮内 steer 注入，回合结束后回写历史增量并执行后处理。
/// </summary>
[Register(typeof(IChatMiddleware), ServiceLifetime.Singleton)]
public sealed partial class QueryLoopMiddleware : ServiceEntity, IChatMiddleware {
    private readonly IBackgroundNotificationHandler _notificationHandler;
    private readonly ITelemetryRecorder _telemetryRecorder;
    private readonly IChatContextManager _contextManager;
    private readonly QueryLoopServices? _services;
    private readonly ILoopDetectionStrategy _loopDetectionStrategy;
    private readonly ILogger<QueryLoopMiddleware>? _logger;
    private readonly ISteerInputMailbox? _steerMailbox;
    private readonly IQueryEngine _queryEngine;

    /// <summary>
    /// 初始化聊天循环中间件。
    /// </summary>
    /// <param name="notificationHandler">后台通知处理器</param>
    /// <param name="telemetryRecorder">遥测记录器</param>
    /// <param name="contextManager">聊天上下文管理器</param>
    /// <param name="queryEngine">查询引擎（唯一循环实现）</param>
    /// <param name="services">聊天循环可选服务聚合</param>
    /// <param name="loopDetectionStrategy">循环检测策略，null 时使用 InformationEntropyGuardian</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="steerMailbox">轮内注入邮箱（可选；null 时 steer 事件永不产生）</param>
    public QueryLoopMiddleware(
        IBackgroundNotificationHandler notificationHandler,
        ITelemetryRecorder telemetryRecorder,
        IChatContextManager contextManager,
        IQueryEngine queryEngine,
        QueryLoopServices? services = null,
        ILoopDetectionStrategy? loopDetectionStrategy = null,
        ILogger<QueryLoopMiddleware>? logger = null,
        ISteerInputMailbox? steerMailbox = null) {
        _notificationHandler = notificationHandler;
        _telemetryRecorder = telemetryRecorder;
        _contextManager = contextManager;
        _queryEngine = queryEngine ?? throw new ArgumentNullException(nameof(queryEngine));
        _services = services;
        _logger = logger;
        _steerMailbox = steerMailbox;
        _loopDetectionStrategy = loopDetectionStrategy ?? new InformationEntropyGuardian(logger: logger);
    }

    /// <summary>
    /// 处理聊天事件流 — 整轮循环委托给 QueryEngine（TASK103 单循环）。
    /// </summary>
    public async IAsyncEnumerable<ChatStreamEvent> InvokeAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct) {
        await foreach (var evt in RunQueryBackendAsync(context, next, ct).ConfigureAwait(false)) {
            yield return evt;
        }
    }

    /// <summary>查询后端等待引擎 chunk 的轮询间隔（毫秒）— 期间排空子代理事件。</summary>
    private const int QueryBackendPollIntervalMs = 50;

    /// <summary>查询后端 chunk 通道容量 — 有界通道向引擎施加背压，防止消费端停滞时无限堆积。</summary>
    private const int QueryBackendChannelCapacity = 256;

    /// <summary>
    /// 查询后端运行状态 — 跨 chunk 累积的收尾数据。
    /// </summary>
    private sealed class QueryBackendState {
        /// <summary>总工具调用次数</summary>
        public int TotalToolCalls { get; set; }
        /// <summary>LLM 调用轮次</summary>
        public int TotalTurns { get; set; }
        /// <summary>本轮是否出现工具调用</summary>
        public bool HasToolCall { get; set; }
        /// <summary>最终 token 用量</summary>
        public TokenUsage? FinalUsage { get; set; }
        /// <summary>最终模型 ID</summary>
        public string? FinalModelId { get; set; }
        /// <summary>最终终止原因</summary>
        public string? FinalStopReason { get; set; }
        /// <summary>当前步文本（文本循环检测输入 — 每次工具调用边界重置）</summary>
        public StringBuilder Text { get; } = new();
        /// <summary>当前步是否出现工具调用（决定收尾时是否做纯文本循环检测）</summary>
        public bool StepHadToolCall { get; set; }
        /// <summary>是否已记录首 token 延迟</summary>
        public bool FirstTokenRecorded { get; set; }
    }

    /// <summary>
    /// 把整轮 ReAct 循环委托给 IQueryEngine：
    /// 历史快照剥离当前用户消息后传入（QueryEngine 会重新加入），输出经适配器转为聊天事件；
    /// 等待引擎输出期间持续合流子代理事件，结束后回写历史增量并执行回合级后处理。
    /// </summary>
    /// <param name="context">聊天中间件上下文。</param>
    /// <param name="next">下游聊天中间件委托。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>聊天事件流。</returns>
    private async IAsyncEnumerable<ChatStreamEvent> RunQueryBackendAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        [EnumeratorCancellation] CancellationToken ct) {
        Diag.WriteLine($"[LOOP-QUERY] backend=query session={context.SessionId} gate={context.RequireRoundGate}");

        await _notificationHandler.ProcessPendingNotificationsAsync(ct).ConfigureAwait(false);

        var history = await BuildQueryHistoryAsync(context, ct).ConfigureAwait(false);
        var baseCount = history.Count;
        var steerEvents = new ConcurrentQueue<ChatStreamEvent>();
        var options = BuildQueryOptions(context, steerEvents);
        var subAgentChannel = context.SubAgentEvents ?? new SubAgentEventChannel();
        context.SubAgentEvents = subAgentChannel;
        var state = new QueryBackendState();

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using var subAgentScope = subAgentChannel.EnterScope();
        var chunkChannel = Channel.CreateBounded<QueryStreamChunk>(new BoundedChannelOptions(QueryBackendChannelCapacity) {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var pumpTask = PumpQueryChunksAsync(context.Message, history, options, chunkChannel.Writer, linkedCts.Token);

        await _telemetryRecorder.RecordTurnTelemetryAsync(history, 0).ConfigureAwait(false);
        context.Timing.StartLlmCall();
        try {
            await foreach (var evt in ConsumeQueryBackendAsync(chunkChannel.Reader, context, subAgentChannel, state, steerEvents, linkedCts.Token).ConfigureAwait(false)) {
                yield return evt;
            }
            await pumpTask.ConfigureAwait(false);
        } finally {
            linkedCts.Cancel();
            await AwaitPumpQuietlyAsync(pumpTask).ConfigureAwait(false);
            context.Timing.StopLlmCall();
            await PersistQueryDeltaQuietlyAsync(history, baseCount, context.IsDryRun).ConfigureAwait(false);
        }

        foreach (var evt in DrainSubAgentEvents(subAgentChannel)) {
            yield return evt;
        }
        subAgentChannel.Complete();

        context.TotalToolCalls = state.TotalToolCalls;
        context.FinalUsage = state.FinalUsage;
        context.FinalModelId = state.FinalModelId;
        if (state.TotalTurns > 0)
            context.Timing.LlmCallCount = state.TotalTurns;

        await RunPostTurnCallbacksAsync(context, state.TotalToolCalls, state.FinalUsage, ct).ConfigureAwait(false);

        await foreach (var evt in CompleteTurnAsync(context, next, state.FinalUsage, state.FinalModelId, state.FinalStopReason, ct).ConfigureAwait(false)) {
            yield return evt;
        }
    }

    /// <summary>剥离当前用户消息后的查询历史 — Preprocessor 已落库，QueryEngine 会重新加入，避免重复。</summary>
    private async Task<MessageList> BuildQueryHistoryAsync(ChatMiddlewareContext context, CancellationToken ct) {
        var snapshot = await _contextManager.GetMessageListAsync(ct).ConfigureAwait(false);
        var history = new MessageList(snapshot);
        if (history.Count > 0) {
            var last = history[history.Count - 1];
            if (last.Role == MessageRole.User && string.Equals(last.Content, context.Message, StringComparison.Ordinal))
                history.RemoveAt(history.Count - 1);
        }
        return history;
    }

    /// <summary>构造查询选项 — 闸门、会话标识、聊天执行设置、步首 steer 钩子与会话级模型/力度覆盖。</summary>
    private QueryOptions BuildQueryOptions(ChatMiddlewareContext context, ConcurrentQueue<ChatStreamEvent> steerEvents) => new() {
        SessionId = context.SessionId,
        RequireRoundGate = context.RequireRoundGate,
        ExecutionOverrides = context.ExecutionSettings,
        ModelId = context.ToolUseContext.ModelOverride,
        EffortLevel = EffortLevelHelper.ParseEffortLevel(context.ToolUseContext.Effort),
        OnIterationStart = (history, _) => InjectIterationInputsAsync(context, history, steerEvents),
        HasPendingIterationInput = () => _steerMailbox?.HasPending(context.SessionId) == true
            || _notificationHandler.HasPendingNotifications,
    };

    /// <summary>步首注入轮内输入 — steer 输入与后台任务通知同时进入查询实时历史，保证 ask 被回调打断后模型能立即看到结果。</summary>
    private Task InjectIterationInputsAsync(
        ChatMiddlewareContext context, MessageList history, ConcurrentQueue<ChatStreamEvent> steerEvents) {
        DrainSteerIntoQueryHistory(context, history, steerEvents);
        InjectPendingBackgroundNotifications(history);
        return Task.CompletedTask;
    }

    /// <summary>步首注入 steer — 写入查询实时历史并排队 SteerInput 事件，供消费循环上屏。</summary>
    private void DrainSteerIntoQueryHistory(
        ChatMiddlewareContext context, MessageList history, ConcurrentQueue<ChatStreamEvent> steerEvents) {
        if (_steerMailbox is null)
            return;

        var pending = _steerMailbox.DrainAll(context.SessionId);
        foreach (var text in pending) {
            history.AddUserMessage(text);
            steerEvents.Enqueue(ChatStreamEvent.SteerInput(text));
            Diag.WriteLine($"[STEER-QUERY] 轮内注入进入查询历史 session={context.SessionId} 长度={text.Length}");
        }
    }

    /// <summary>步首注入后台任务通知 — 从通知队列排空并写入查询实时历史。</summary>
    private void InjectPendingBackgroundNotifications(MessageList history) {
        foreach (var notification in _notificationHandler.DrainPendingNotificationTexts()) {
            history.AddUserMessage(notification);
            Diag.WriteLine($"[BACKGROUND-NOTIFY] 轮内注入进入查询历史 长度={notification.Length}");
        }
    }

    /// <summary>把查询引擎输出泵入通道 — 引擎异常通过 pumpTask 回抛，通道关闭由 finally 保证。</summary>
    private async Task PumpQueryChunksAsync(
        string userInput,
        MessageList history,
        QueryOptions options,
        ChannelWriter<QueryStreamChunk> writer,
        CancellationToken ct) {
        try {
            await foreach (var chunk in _queryEngine.QueryAsync(userInput, history, options, ct).ConfigureAwait(false)) {
                await writer.WriteAsync(chunk, ct).ConfigureAwait(false);
            }
        } finally {
            writer.TryComplete();
        }
    }

    /// <summary>消费引擎 chunk 并合流子代理事件 — 引擎无输出时每 50ms 排空一次通道。</summary>
    private async IAsyncEnumerable<ChatStreamEvent> ConsumeQueryBackendAsync(
        ChannelReader<QueryStreamChunk> reader,
        ChatMiddlewareContext context,
        SubAgentEventChannel subAgentChannel,
        QueryBackendState state,
        ConcurrentQueue<ChatStreamEvent> steerEvents,
        [EnumeratorCancellation] CancellationToken ct) {
        Task<bool>? pendingRead = null;
        while (true) {
            ct.ThrowIfCancellationRequested();

            while (steerEvents.TryDequeue(out var steerEvt)) {
                yield return steerEvt;
            }

            foreach (var evt in DrainSubAgentEvents(subAgentChannel)) {
                yield return evt;
            }

            while (reader.TryRead(out var chunk)) {
                foreach (var evt in HandleQueryChunk(chunk, context, state)) {
                    yield return evt;
                }
            }

            pendingRead ??= reader.WaitToReadAsync(ct).AsTask();
            var delay = Task.Delay(QueryBackendPollIntervalMs, ct);
            if (await Task.WhenAny(pendingRead, delay).ConfigureAwait(false) != pendingRead)
                continue;

            if (!await pendingRead.ConfigureAwait(false))
                yield break;
            pendingRead = null;
        }
    }

    /// <summary>排空子代理事件通道 — 无通道时返回空。</summary>
    private static IEnumerable<ChatStreamEvent> DrainSubAgentEvents(SubAgentEventChannel? channel) =>
        channel is null ? [] : channel.TryDrain();

    /// <summary>单个查询 chunk → 聊天事件（含循环检测与完成数据捕获；Complete 由收尾统一发出）。</summary>
    private IEnumerable<ChatStreamEvent> HandleQueryChunk(QueryStreamChunk chunk, ChatMiddlewareContext context, QueryBackendState state) {
        if (chunk.Type == AgentStreamChunkType.Content) {
            state.Text.Append(chunk.Content);
            RecordFirstToken(context, state);
        } else if (chunk.Type is AgentStreamChunkType.ThinkingStart or AgentStreamChunkType.Thinking) {
            RecordFirstToken(context, state);
        } else if (chunk.Type == AgentStreamChunkType.ToolCallStart) {
            state.HasToolCall = true;
            state.StepHadToolCall = true;
            state.Text.Clear();
            state.TotalToolCalls++;
            if (_loopDetectionStrategy.TryGetToolCallLoop(chunk.ToolName ?? string.Empty, JsonArgumentParser.Parse(chunk.ToolArguments), out var toolLoop)) {
                yield return ChatStreamEvent.LoopDetected(toolLoop.TriggerCount, toolLoop.ToolCallCount, toolLoop.Reason);
            }
        } else if (chunk.Type == AgentStreamChunkType.ToolCallEnd) {
            state.StepHadToolCall = false;
            state.Text.Clear();
        } else if (chunk.Type == AgentStreamChunkType.Complete) {
            state.FinalUsage = chunk.Usage;
            state.FinalModelId = chunk.ModelId;
            state.FinalStopReason = chunk.StopReason;
            state.TotalTurns = chunk.TotalTurns;
            if (chunk.TotalToolCalls > state.TotalToolCalls)
                state.TotalToolCalls = chunk.TotalToolCalls;
            if (!state.StepHadToolCall && state.Text.Length > 0 && _loopDetectionStrategy.TryGetTextLoop(state.Text.ToString(), out var textLoop)) {
                yield return ChatStreamEvent.LoopDetected(textLoop.TriggerCount, textLoop.ToolCallCount, textLoop.Reason);
            }
            yield break;
        }

        var evt = QueryStreamChunkAdapter.ToChatStreamEvent(chunk);
        if (evt is not null)
            yield return evt;
    }

    /// <summary>记录首 token 延迟（仅首次）— LLM 计时已由 StartLlmCall 启动。</summary>
    private static void RecordFirstToken(ChatMiddlewareContext context, QueryBackendState state) {
        if (state.FirstTokenRecorded)
            return;
        state.FirstTokenRecorded = true;
        context.Timing.FirstTokenLatencyMs = context.Timing.LlmTotalMs;
    }

    /// <summary>回写查询历史增量 — 第一条是 QueryEngine 重新加入的当前用户消息（Preprocessor 已落库），跳过。</summary>
    private async Task PersistQueryDeltaQuietlyAsync(MessageList history, int baseCount, bool isDryRun) {
        if (isDryRun)
            return;

        try {
            for (var i = baseCount + 1; i < history.Count; i++) {
                await PersistMessageAsync(history[i], CancellationToken.None).ConfigureAwait(false);
            }
        } catch (Exception ex) {
            _logger?.LogError(ex, "[QueryLoopMiddleware] 查询后端历史增量回写失败");
        }
    }

    /// <summary>按角色回写单条消息 — 复用既有上下文管理器方法，保持会话存储不变量。</summary>
    private async Task PersistMessageAsync(ApiMessage message, CancellationToken ct) {
        switch (message.Role) {
            case MessageRole.Assistant when message.Metadata.Count > 0:
                await _contextManager.AddAssistantToolCallMessageAsync(message.Content, message.Metadata, ct).ConfigureAwait(false);
                break;
            case MessageRole.Assistant:
                if (!string.IsNullOrWhiteSpace(message.Content))
                    await _contextManager.AddAssistantMessageAsync(message.Content, ct).ConfigureAwait(false);
                break;
            case MessageRole.Tool:
                await _contextManager.AddToolResultMessageAsync(
                    message.Content ?? string.Empty,
                    message.Metadata,
                    message.ContentBlocks.Count > 0 ? message.ContentBlocks : null,
                    ct).ConfigureAwait(false);
                break;
            case MessageRole.System:
                await _contextManager.AddSystemMessageAsync(message.Content ?? string.Empty, ct).ConfigureAwait(false);
                break;
            case MessageRole.User:
                await _contextManager.AddUserMessageAsync(message.Content ?? string.Empty, cancellationToken: ct).ConfigureAwait(false);
                break;
            default:
                break;
        }
    }

    /// <summary>静默等待引擎泵任务结束 — 取消/异常已在主路径或收尾中处理，避免掩盖原始错误。</summary>
    private static async Task AwaitPumpQuietlyAsync(Task pumpTask) {
        try {
            await pumpTask.ConfigureAwait(false);
        } catch (Exception ex) {
            if (ex is not OperationCanceledException)
                Diag.WriteLine($"[LOOP-QUERY] 引擎泵收尾异常: {ex.Message}");
        }
    }

    /// <summary>回合级后处理 — 空闲检测、延迟工具发现同步与后采样回调。</summary>
    private async Task RunPostTurnCallbacksAsync(ChatMiddlewareContext context, int totalToolCalls, TokenUsage? finalUsage, CancellationToken ct) {
        _services?.IdleDetector?.RecordAssistantTurn(null);
        await _contextManager.SyncDiscoveredToolsFromHistoryAsync(ct).ConfigureAwait(false);

        if (_services?.PostSamplingCallbacks is not null) {
            var sessionId = (_contextManager is ChatContextManager cm) ? cm.SessionId : null;
            var postSamplingCtx = new PostSamplingContext {
                EstimatedTokenCount = finalUsage?.TotalTokens ?? 0,
                ToolCallsSinceLastExtraction = totalToolCalls,
                QuerySource = "repl_main_thread",
                SessionId = sessionId,
                CancellationToken = ct,
                EventConsumer = context.EventConsumer
            };
            await _services.PostSamplingCallbacks.FireAsync(postSamplingCtx).ConfigureAwait(false);
        }
    }

    /// <summary>透传下游事件并统一发出 Done — 下游若已发 Complete，其 stopReason 优先，其次用查询引擎终止原因。</summary>
    private static async IAsyncEnumerable<ChatStreamEvent> CompleteTurnAsync(
        ChatMiddlewareContext context,
        StreamMiddlewareDelegate<ChatMiddlewareContext, ChatStreamEvent> next,
        TokenUsage? finalUsage,
        string? finalModelId,
        string? fallbackStopReason,
        [EnumeratorCancellation] CancellationToken ct) {
        string? innerStopReason = null;
        await foreach (var evt in next(context, ct).ConfigureAwait(false)) {
            if (evt.Type == ChatStreamEventType.Complete)
                innerStopReason = evt.StopReason;
            yield return evt;
        }

        yield return ChatStreamEvent.Done(finalUsage, finalModelId, innerStopReason ?? fallbackStopReason);
    }
}
