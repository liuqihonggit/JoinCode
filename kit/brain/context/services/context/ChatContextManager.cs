namespace Core.Context;

/// <summary>
/// 默认上下文窗口解析器 — 当 DI 未注入 IContextWindowResolver 时使用
/// 返回固定默认值 200K（对齐 TS MODEL_CONTEXT_WINDOW_DEFAULT）
/// </summary>
internal sealed class DefaultContextWindowResolver : IContextWindowResolver {
    /// <summary>解析当前上下文窗口大小，返回默认值 200K。</summary>
    public int ResolveCurrentContextWindow() => 200_000;
}

/// <summary>
/// ChatContextManager 可选依赖聚合
/// </summary>
public sealed record ChatContextOptions {
    /// <summary>上下文折叠执行器（可选，null 时禁用折叠）</summary>
    public ContextFoldExecutor? FoldExecutor { get; init; }
    /// <summary>上下文折叠阈值配置（可选，null 时使用默认值）</summary>
    public ContextFoldThresholds? Thresholds { get; init; }
    /// <summary>上下文窗口解析器（可选，null 时使用默认 200K 窗口）</summary>
    public IContextWindowResolver? ContextWindowResolver { get; init; }
    /// <summary>会话元数据存储（可选，null 时不持久化会话统计）</summary>
    public ISessionMetaStore? MetaStore { get; init; }
    /// <summary>会话统计追踪器（可选）</summary>
    public SessionStats? SessionStats { get; init; }
    /// <summary>会话标识（可选，null 时使用进程主 ID）</summary>
    public string? SessionId { get; init; }
    /// <summary>遥测服务（可选）</summary>
    public ITelemetryService? TelemetryService { get; init; }
    /// <summary>时钟服务（可选，null 时使用系统时钟）</summary>
    public IClockService? Clock { get; init; }
    /// <summary>供应商 BaseUrl（用于解析缓存 TTL）</summary>
    public string? ProviderBaseUrl { get; init; }
}

/// <summary>
/// 聊天上下文管理器 — 编排多个单一职责服务，管理系统提示词、对话历史、工具规格、上下文折叠和缓存失效检测
/// <para>按 SessionId 隔离对话历史，支持多会话切换；使用 Actor 邮箱管道串行化所有操作，消除显式锁 — TASK001</para>
/// <para>TASK031-D1: 拆分为 7 个单一数据源服务: SessionStore + PromptStore + ToolSpecStore + CacheBreakStore + ContextFoldService + RewindService + ContextPersistenceService</para>
/// </summary>
[Register(typeof(IChatContextManager), ServiceLifetime.Singleton)]
public sealed class ChatContextManager : IChatContextManager, IAsyncDisposable {
    private readonly ILogger<ChatContextManager> _logger;
    private readonly ChatContextActor _actor;
    private readonly IContextWindowResolver _contextWindowResolver;
    private readonly ITelemetryService? _telemetryService;
    private string _sessionId;
    private int _callSeq;
    private int _disposed;

    private readonly ISessionStore _sessionStore;
    private readonly IPromptStore _promptStore;
    private readonly IToolSpecStore _toolSpecStore;
    private readonly ICacheBreakStore _cacheBreakStore;
    private readonly IContextFoldService _foldService;
    private readonly IRewindService _rewindService;
    private readonly IContextPersistenceService _persistenceService;

    /// <summary>
    /// 初始化聊天上下文管理器，注入状态服务和可选依赖聚合
    /// </summary>
    /// <param name="stateService">状态持久化服务</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="options">可选依赖聚合，null 时使用默认值</param>
    public ChatContextManager(
        IStateService stateService,
        ILogger<ChatContextManager> logger,
        ChatContextOptions? options = null) {
        _logger = logger;

        var foldExecutor = options?.FoldExecutor;
        var thresholds = options?.Thresholds ?? ContextFoldThresholds.Default;
        _contextWindowResolver = options?.ContextWindowResolver ?? new DefaultContextWindowResolver();
        var metaStore = options?.MetaStore;
        var sessionStats = options?.SessionStats;
        // T10：无显式会话时用进程主 ID（五段式真 ID），禁止 "default" 字面量落盘
        _sessionId = options?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
        _telemetryService = options?.TelemetryService;
        var clock = options?.Clock ?? SystemClockService.Instance;
        var providerBaseUrl = options?.ProviderBaseUrl;

        var promptStore = new SystemPromptStore();
        _sessionStore = new SessionStore(_sessionId);
        _promptStore = promptStore;
        _toolSpecStore = new ToolSpecStore();
        _cacheBreakStore = new CacheBreakStore();
        _foldService = new ContextFoldService(_sessionStore, _toolSpecStore, _cacheBreakStore, _contextWindowResolver, foldExecutor, thresholds, _telemetryService, logger);
        _rewindService = new RewindService(_sessionStore, _promptStore, _telemetryService, logger);
        _persistenceService = new ContextPersistenceService(_sessionStore, _promptStore, stateService, metaStore, sessionStats, _contextWindowResolver, thresholds, _telemetryService, clock, providerBaseUrl, logger);

        _actor = new ChatContextActor(this, logger);
    }

    /// <summary>
    /// 分配下一个链路调用序号 — 线程安全，Interlocked 递增
    /// </summary>
    public int NextCallSeq() => Interlocked.Increment(ref _callSeq);

    /// <summary>
    /// 生成链路调用 ID — 格式: {sessionId短码}.{序号}
    /// </summary>
    public string NextCallId() {
        var seq = NextCallSeq();
        var shortId = _sessionId.Length > 4 ? _sessionId[..4] : _sessionId;
        return $"{shortId}.{seq}";
    }

    /// <summary>
    /// 当前会话标识
    /// </summary>
    public string SessionId => _sessionId;

    /// <summary>当前对话日志条目数 — transcript 增量持久化的快照基准</summary>
    public int CurrentMessageCount => _sessionStore.Count;

    /// <summary>切换会话 — 按 sessionId 隔离对话历史，切回时自动恢复对应桶</summary>
    public void SwitchSession(string sessionId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _sessionId = sessionId;
        _sessionStore.SwitchSession(sessionId);
    }

    /// <summary>
    /// 从持久化存储加载聊天上下文，恢复系统提示词和对话历史
    /// </summary>
    public async Task LoadContextAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        _actor.Tell(new LoadContextCmd(reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加用户消息到对话日志
    /// </summary>
    public async Task AddUserMessageAsync(string content, MessageOriginKind? originKind = null, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var reply = new TaskCompletionSource();
        _actor.Tell(new AddUserMessageCmd(content, originKind, reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加压缩摘要消息到对话日志（标记 isCompactSummary 元数据）
    /// </summary>
    public async Task AddCompactSummaryMessageAsync(string content, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var reply = new TaskCompletionSource();
        _actor.Tell(new AddCompactSummaryCmd(content, reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加助手消息到对话日志
    /// </summary>
    public async Task AddAssistantMessageAsync(string content, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var reply = new TaskCompletionSource();
        _actor.Tell(new AddAssistantMessageCmd(content, reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加助手工具调用消息（含元数据）到对话日志
    /// </summary>
    public async Task AddAssistantToolCallMessageAsync(string? content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        _actor.Tell(new AddAssistantToolCallCmd(content, metadata, reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加工具结果消息到对话日志
    /// </summary>
    public async Task AddToolResultMessageAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        _actor.Tell(new AddToolResultCmd(content, metadata, reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加包含多模态内容的工具结果消息 — 对齐 TS BashTool image output
    /// </summary>
    public async Task AddToolResultMessageAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, IReadOnlyList<ToolContent>? contentBlocks, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        _actor.Tell(new AddToolResultWithBlocksCmd(content, metadata, contentBlocks, reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加系统消息到对话日志
    /// </summary>
    public async Task AddSystemMessageAsync(string content, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var reply = new TaskCompletionSource();
        _actor.Tell(new AddSystemMessageCmd(content, reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加动态系统消息，该消息独立于对话日志，会随前缀一起组装
    /// </summary>
    public async Task AddDynamicSystemMessageAsync(string content, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var reply = new TaskCompletionSource();
        _actor.Tell(new AddDynamicSystemMessageCmd(content, reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 清空所有动态系统消息
    /// </summary>
    public async Task ClearDynamicSystemMessagesAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        _actor.Tell(new ClearDynamicSystemMessagesCmd(reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 清空所有对话消息和动态系统消息，保留静态系统提示词
    /// </summary>
    public async Task ClearMessagesAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        _actor.Tell(new ClearMessagesCmd(reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新静态系统提示词，清空缓存
    /// </summary>
    public async Task UpdateSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);

        var reply = new TaskCompletionSource();
        _actor.Tell(new UpdateSystemPromptCmd(systemPrompt, reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取组装后的完整消息列表（静态系统提示词 + 动态系统消息 + 对话日志）
    /// </summary>
    public async Task<MessageList> GetMessageListAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<MessageList>();
        _actor.Tell(new GetMessageListCmd(reply));
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 将当前聊天上下文持久化保存，包括系统提示词、对话历史和会话统计
    /// </summary>
    public async Task SaveContextAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        _actor.Tell(new SaveContextCmd(reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 根据本次 token 用量决定是否需要折叠上下文
    /// 缓存命中且低于硬阈值时返回 <see cref="ContextFoldDecision.Deferred"/>，推迟折叠以保留缓存前缀
    /// </summary>
    public ContextFoldDecision DecideAfterUsage(TokenUsage usage, bool alreadyFoldedThisTurn = false) {
        return _foldService.DecideAfterUsage(usage, alreadyFoldedThisTurn);
    }

    /// <summary>
    /// 在发送请求前预判是否需要折叠，基于当前消息和工具规格估算 token 占用
    /// </summary>
    public PreflightDecision DecidePreflight(IReadOnlyList<ToolSpec> toolSpecs) {
        var messages = AssembleMessages();
        return _foldService.DecidePreflight(toolSpecs, messages);
    }

    /// <summary>
    /// 根据折叠决策执行上下文折叠操作（普通/激进/摘要退出）
    /// </summary>
    public async Task<ContextFoldResult> FoldIfNeededAsync(ContextFoldDecision decision, string? agentId = null, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<ContextFoldResult>();
        _actor.Tell(new FoldIfNeededCmd(decision, agentId, reply));
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取当前上下文窗口的最大 token 数
    /// </summary>
    public int GetContextMaxTokens() => _contextWindowResolver.ResolveCurrentContextWindow();

    /// <summary>
    /// 撤回最后一轮对话（SP-3 安全点）。尾部变更，前缀缓存仍命中。
    /// </summary>
    public async Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<RewindResult>();
        _actor.Tell(new RewindLastTurnCmd(reply));
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 撤回到指定消息索引（SP-5 安全点）。截断 [index, Count) 的消息。
    /// 缓存失效是预期行为，下一轮重新积累。
    /// </summary>
    public async Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<RewindResult>();
        _actor.Tell(new RewindToMessageIndexCmd(messageIndex, reply));
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 清空全部对话历史（SP-0 安全点）。前缀保留，历史清空。
    /// </summary>
    public async Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<RewindResult>();
        _actor.Tell(new RewindToStartCmd(reply));
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新当前工具规格列表（用于缓存失效检测）。MCP 工具同步时调用。
    /// </summary>
    public async Task UpdateToolSpecsAsync(IReadOnlyList<ToolSpec> toolSpecs, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(toolSpecs);

        var reply = new TaskCompletionSource();
        _actor.Tell(new UpdateToolSpecsCmd(toolSpecs, reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 记录当前提示词前缀状态快照，用于后续缓存失效检测
    /// </summary>
    public async Task<PromptStateSnapshot> RecordPromptStateAsync(string? agentId = null, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<PromptStateSnapshot>();
        _actor.Tell(new RecordPromptStateCmd(agentId, reply));
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 检测缓存是否失效，对比快照与当前前缀状态并结合 token 用量判断
    /// </summary>
    public async Task<CacheBreakResult> CheckCacheBreakAsync(PromptStateSnapshot snapshot, TokenUsage usage, string? agentId = null, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(usage);

        var reply = new TaskCompletionSource<CacheBreakResult>();
        _actor.Tell(new CheckCacheBreakCmd(snapshot, usage, agentId, reply));
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取已发现的工具集合
    /// </summary>
    public DiscoveredToolSet GetDiscoveredTools() {
        return _toolSpecStore.DiscoveredTools;
    }

    /// <summary>
    /// 获取延迟加载的工具信息列表（主要是 MCP 工具）
    /// </summary>
    public IEnumerable<DeferredToolInfo> GetDeferredTools() {
        return _toolSpecStore.DeferredTools;
    }

    /// <summary>
    /// 从对话历史中提取已发现的工具名称并同步到已发现工具集合
    /// </summary>
    public async Task SyncDiscoveredToolsFromHistoryAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        _actor.Tell(new SyncDiscoveredToolsFromHistoryCmd(reply));
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步释放资源，释放内部 Actor
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await _actor.DisposeAsync().ConfigureAwait(false);
    }

    private List<ApiMessage> AssembleMessages() {
        var messages = new List<ApiMessage>();

        var systemMessages = _promptStore.GetOrCreateCachedSystemMessages();
        messages.AddRange(systemMessages);

        foreach (var msg in _sessionStore.ToMessages()) {
            messages.Add(msg);
        }

        return messages;
    }

    /// <summary>
    /// 记录提示词前缀状态快照内部实现 — 编排 PromptStore + ToolSpecStore + CacheBreakStore
    /// </summary>
    private Task<PromptStateSnapshot> RecordPromptStateInternalAsync(string? agentId, CancellationToken cancellationToken) {
        var prefix = new ImmutablePrefix(_promptStore.StaticPrompt, _toolSpecStore.CurrentSpecs, []);
        var dynamicContent = _promptStore.GetDynamicContent();
        var snapshot = _cacheBreakStore.RecordPromptState(agentId, prefix, dynamicContent, _sessionStore.ToMessages());

        var toolSpecsBytes = _toolSpecStore.CurrentSpecs.Sum(t =>
            System.Text.Encoding.UTF8.GetByteCount(t.Name) +
            (t.Description != null ? System.Text.Encoding.UTF8.GetByteCount(t.Description) : 0) +
            (t.InputSchemaJson != null ? System.Text.Encoding.UTF8.GetByteCount(t.InputSchemaJson) : 0));
        var systemBytes = System.Text.Encoding.UTF8.GetByteCount(_promptStore.StaticPrompt);
        var estimatedTokens = ContextFoldDecider.EstimateTokenCount(
            [new ApiMessage(MessageRole.System, _promptStore.StaticPrompt)],
            _toolSpecStore.CurrentSpecs);

        _logger.LogInformation(
            "前缀状态快照已记录，SystemHash={SystemHash}, SystemBytes={SystemBytes}, ToolCount={ToolCount}, ToolNamesHash={ToolNamesHash}, ToolSpecsBytes={ToolSpecsBytes}, EstimatedTokens={EstimatedTokens}",
            snapshot.SystemPromptHash, systemBytes, snapshot.ToolCount, snapshot.ToolNamesHash, toolSpecsBytes, estimatedTokens);

        return Task.FromResult(snapshot);
    }

    /// <summary>
    /// 检测缓存失效内部实现 — 编排 PromptStore + ToolSpecStore + CacheBreakStore
    /// </summary>
    private Task<CacheBreakResult> CheckCacheBreakInternalAsync(PromptStateSnapshot snapshot, TokenUsage usage, string? agentId, CancellationToken cancellationToken) {
        var currentPrefix = new ImmutablePrefix(_promptStore.StaticPrompt, _toolSpecStore.CurrentSpecs, []);
        var currentDynamicContent = _promptStore.GetDynamicContent();
        var result = _cacheBreakStore.CheckCacheBreak(agentId, snapshot, currentPrefix, currentDynamicContent, usage, _sessionStore.ToMessages());

        if (result.BreakDetected) {
            _logger.LogWarning("缓存失效检测: Kind={Kind}, Detail={Detail}, CacheReadTokens={CacheReadTokens}",
                result.Kind, result.Detail, usage.CacheReadInputTokens);
        } else {
            _logger.LogInformation("缓存失效检测: 无失效，前缀稳定，CacheReadTokens={CacheReadTokens}, CacheCreationTokens={CacheCreationTokens}",
                usage.CacheReadInputTokens, usage.CacheCreationInputTokens);
        }

        return Task.FromResult(result);
    }

    /// <summary>
    /// 聊天上下文 Actor — 串行化所有操作，消除显式锁 — TASK001
    /// <para>命令通过 Channel 投递，Consumer 单线程串行处理，天然无竞态。</para>
    /// </summary>
    private sealed class ChatContextActor : ActorBase<ChatContextCommand, Unit> {
        private readonly ChatContextManager _owner;
        private readonly ILogger<ChatContextManager> _logger;

        /// <summary>构造聊天上下文 Actor。</summary>
        /// <param name="owner">所属聊天上下文管理器</param>
        /// <param name="logger">日志记录器</param>
        public ChatContextActor(ChatContextManager owner, ILogger<ChatContextManager> logger) : base() {
            _owner = owner;
            _logger = logger;
        }

        /// <summary>Ask 模式等待回复（无返回值）— 暴露 protected AskAwait 供 ChatContextManager 调用</summary>
        public async Task AskReplyAsync(TaskCompletionSource tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        /// <summary>Ask 模式等待回复（有返回值）— 暴露 protected AskAwait 供 ChatContextManager 调用</summary>
        public async Task<T> AskReplyAsync<T>(TaskCompletionSource<T> tcs, CancellationToken ct = default)
            => await base.AskAwait(tcs, ct).ConfigureAwait(false);

        protected override void Handle(ChatContextCommand cmd, CancellationToken ct) { _ = HandleAsyncImpl(cmd, ct); }
        private async ValueTask HandleAsyncImpl(ChatContextCommand cmd, CancellationToken ct) {
            try {
                switch (cmd) {
                    case LoadContextCmd(var reply):
                    await _owner._persistenceService.LoadAsync(_owner._sessionId, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case AddUserMessageCmd(var content, var originKind, var reply):
                    _owner.AddUserMessageInternal(content, originKind);
                    reply.SetResult();
                    break;
                    case AddCompactSummaryCmd(var content, var reply):
                    _owner.AddCompactSummaryInternal(content);
                    reply.SetResult();
                    break;
                    case AddAssistantMessageCmd(var content, var reply):
                    _owner._sessionStore.Append(new ApiMessage(MessageRole.Assistant, content));
                    _logger.LogDebug("已添加助手消息，当前对话数: {Count}", _owner._sessionStore.Count);
                    reply.SetResult();
                    break;
                    case AddAssistantToolCallCmd(var content, var metadata, var reply):
                    _owner._sessionStore.Append(new ApiMessage(MessageRole.Assistant, content, metadata));
                    _logger.LogDebug("已添加助手工具调用消息，当前对话数: {Count}", _owner._sessionStore.Count);
                    reply.SetResult();
                    break;
                    case AddToolResultCmd(var content, var metadata, var reply):
                    _owner._sessionStore.Append(new ApiMessage(MessageRole.Tool, content, metadata));
                    _logger.LogDebug("已添加工具结果消息，当前对话数: {Count}", _owner._sessionStore.Count);
                    reply.SetResult();
                    break;
                    case AddToolResultWithBlocksCmd(var content, var metadata, var contentBlocks, var reply):
                    _owner._sessionStore.Append(new ApiMessage(MessageRole.Tool, content, metadata) { ContentBlocks = contentBlocks ?? [] });
                    _logger.LogDebug("已添加工具结果消息(含多模态)，当前对话数: {Count}", _owner._sessionStore.Count);
                    reply.SetResult();
                    break;
                    case AddSystemMessageCmd(var content, var reply):
                    _owner._sessionStore.Append(new ApiMessage(MessageRole.System, content));
                    _logger.LogDebug("已添加系统消息，当前对话数: {Count}", _owner._sessionStore.Count);
                    reply.SetResult();
                    break;
                    case AddDynamicSystemMessageCmd(var content, var reply):
                    _owner._promptStore.AddDynamic(content);
                    _logger.LogDebug("已添加动态系统消息");
                    reply.SetResult();
                    break;
                    case ClearDynamicSystemMessagesCmd(var reply):
                    _owner._promptStore.ClearDynamic();
                    _logger.LogDebug("已清空动态系统消息");
                    reply.SetResult();
                    break;
                    case ClearMessagesCmd(var reply):
                    _owner._sessionStore.CompactInPlace([]);
                    _owner._promptStore.ResetCache();
                    _logger.LogInformation("聊天消息已清空，保留静态系统提示词");
                    reply.SetResult();
                    break;
                    case UpdateSystemPromptCmd(var systemPrompt, var reply):
                    _owner._promptStore.Update(systemPrompt);
                    _logger.LogInformation("静态系统提示词已更新，长度: {Len}", _owner._promptStore.StaticPrompt.Length);
                    reply.SetResult();
                    break;
                    case GetMessageListCmd(var reply):
                    reply.SetResult(MessageList.FromList(_owner.AssembleMessages()));
                    break;
                    case SaveContextCmd(var reply):
                    await _owner._persistenceService.SaveAsync(_owner._sessionId, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case FoldIfNeededCmd(var decision, var agentId, var reply):
                    reply.SetResult(await _owner._foldService.FoldIfNeededAsync(decision, agentId, ct).ConfigureAwait(false));
                    break;
                    case RewindLastTurnCmd(var reply):
                    reply.SetResult(_owner._rewindService.RewindLastTurn());
                    break;
                    case RewindToMessageIndexCmd(var messageIndex, var reply):
                    reply.SetResult(_owner._rewindService.RewindToMessageIndex(messageIndex));
                    break;
                    case RewindToStartCmd(var reply):
                    reply.SetResult(_owner._rewindService.RewindToStart());
                    break;
                    case UpdateToolSpecsCmd(var toolSpecs, var reply):
                    _owner._toolSpecStore.UpdateSpecs(toolSpecs);
                    _logger.LogDebug("工具规格已更新，当前 {Count} 个工具，{DeferredCount} 个延迟工具",
                        _owner._toolSpecStore.CurrentSpecs.Count, _owner._toolSpecStore.DeferredTools.Count);
                    reply.SetResult();
                    break;
                    case RecordPromptStateCmd(var agentId, var reply):
                    reply.SetResult(await _owner.RecordPromptStateInternalAsync(agentId, ct).ConfigureAwait(false));
                    break;
                    case CheckCacheBreakCmd(var snapshot, var usage, var agentId, var reply):
                    reply.SetResult(await _owner.CheckCacheBreakInternalAsync(snapshot, usage, agentId, ct).ConfigureAwait(false));
                    break;
                    case SyncDiscoveredToolsFromHistoryCmd(var reply):
                    await _owner._toolSpecStore.SyncDiscoveredToolsFromHistoryAsync(_owner.AssembleMessages()).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                }
            } catch (OperationCanceledException) { throw; } catch (Exception ex) { cmd.SetReplyException(ex); }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger.LogWarning(ex, "ChatContextActor 命令处理异常");
    }

    private void AddUserMessageInternal(string content, MessageOriginKind? originKind) {
        var metadata = originKind is null
            ? null
            : new Dictionary<string, JsonElement> {
                [MessageMetadataKeyEnumConstants.Origin] = JsonElementHelper.FromJson($"{{\"kind\":\"{originKind.Value.ToValue()}\"}}")
            };
        _sessionStore.Append(new ApiMessage(MessageRole.User, content, metadata));
        _logger.LogDebug("已添加用户消息，当前对话数: {Count}", _sessionStore.Count);
    }

    private void AddCompactSummaryInternal(string content) {
        _sessionStore.Append(new ApiMessage(MessageRole.User, content, new Dictionary<string, JsonElement> {
            ["isCompactSummary"] = JsonElementHelper.FromBoolean(true)
        }));
        _logger.LogDebug("已添加压缩摘要消息，当前对话数: {Count}", _sessionStore.Count);
    }
}
