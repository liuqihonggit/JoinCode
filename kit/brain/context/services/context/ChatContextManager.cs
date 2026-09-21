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
/// 聊天上下文管理器 — 管理系统提示词、对话历史、工具规格、上下文折叠和缓存失效检测
/// 按 SessionId 隔离对话历史，支持多会话切换；使用 Actor 邮箱管道串行化所有操作，消除显式锁 — TASK001
/// </summary>
[Register(typeof(IChatContextManager), ServiceLifetime.Singleton)]
public partial class ChatContextManager : IChatContextManager, IAsyncDisposable {
    private readonly IStateService _stateService;
    private readonly ILogger<ChatContextManager> _logger;
    private readonly ChatContextActor _actor;
    private readonly ContextFoldExecutor? _foldExecutor;
    private readonly ContextFoldThresholds _thresholds;
    private readonly IContextWindowResolver _contextWindowResolver;
    private readonly ISessionMetaStore? _metaStore;
    private readonly SessionStats? _sessionStats;
    private string _sessionId;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;
    private readonly string? _providerBaseUrl;
    private int _callSeq;
    private int _disposed;

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
    public int CurrentMessageCount => Log.Count;

    /// <summary>切换会话 — 按 sessionId 隔离对话历史，切回时自动恢复对应桶</summary>
    public void SwitchSession(string sessionId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        _sessionId = sessionId;
    }

    private readonly SystemPromptStore _promptStore = new();
    private readonly List<ToolSpec> _currentToolSpecs = [];
    private readonly ConcurrentDictionary<string, AppendOnlyLog> Logs = new();

    /// <summary>当前会话的对话日志 — 按 SessionId 隔离，切换会话时自动分桶</summary>
    private AppendOnlyLog Log => Logs.GetOrAdd(_sessionId, _ => new AppendOnlyLog());

    /// <summary>缓存破坏检测器 — 按 agentId 隔离，主代理(null)和子代理互不干扰基线</summary>
    private readonly ConcurrentDictionary<string, CacheBreakDetector> _cacheBreakDetectorsByAgent = new();
    private CacheBreakDetector GetCacheBreakDetector(string? agentId)
        => _cacheBreakDetectorsByAgent.GetOrAdd(agentId ?? "main", _ => new CacheBreakDetector());

    private readonly DiscoveredToolSet _discoveredTools = new();
    private readonly List<DeferredToolInfo> _deferredTools = [];
    private int _deferredFoldCount;
    private int _consecutiveNoProgressFolds;

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
        _stateService = stateService;
        _logger = logger;

        _foldExecutor = options?.FoldExecutor;
        _thresholds = options?.Thresholds ?? ContextFoldThresholds.Default;
        _contextWindowResolver = options?.ContextWindowResolver ?? new DefaultContextWindowResolver();
        _metaStore = options?.MetaStore;
        _sessionStats = options?.SessionStats;
        // T10：无显式会话时用进程主 ID（五段式真 ID），禁止 "default" 字面量落盘
        _sessionId = options?.SessionId ?? global::Core.Utils.SessionIdFactory.DefaultSessionId;
        _telemetryService = options?.TelemetryService;
        _clock = options?.Clock ?? SystemClockService.Instance;
        _providerBaseUrl = options?.ProviderBaseUrl;
        _actor = new ChatContextActor(this, logger);
    }

    /// <summary>
    /// 从持久化存储加载聊天上下文，恢复系统提示词和对话历史
    /// </summary>
    public async Task LoadContextAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new LoadContextCmd(reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 加载上下文内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private async Task LoadContextInternalAsync(CancellationToken cancellationToken) {
        await using var span = _telemetryService?.StartSpan("context.load", TelemetrySpanKind.Server);
        try {
            var (systemPrompt, chatHistory) = await _stateService.LoadStateAsync(cancellationToken).ConfigureAwait(false);

            _promptStore.Update(systemPrompt ?? string.Empty);
            Log.CompactInPlace([]);

            if (chatHistory is { Count: > 0 }) {
                foreach (var msg in chatHistory) {
                    if (msg.Role != MessageRole.System) {
                        Log.Append(new ApiMessage(msg.Role, msg.Content, msg.Metadata));
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(_promptStore.StaticPrompt)) {
                        _promptStore.Update(msg.Content ?? string.Empty);
                    }
                    continue;
                }
            }

            _logger.LogInformation("聊天上下文已加载，静态前缀长度: {Len}, 对话消息数: {Count}",
                _promptStore.StaticPrompt.Length, Log.Count);

            span?.SetTag("context.message_count", Log.Count);
            span?.SetStatus(TelemetryStatusCode.Ok);

            if (_metaStore is not null && _sessionStats is not null) {
                var meta = await _metaStore.LoadAsync(_sessionId, cancellationToken).ConfigureAwait(false);
                if (meta is not null) {
                    _sessionStats.SeedCarryover(meta.CacheHitTokens, meta.CacheMissTokens, meta.TotalCostUsd);
                    _logger.LogInformation("会话统计已恢复，缓存命中: {Hit}, 未命中: {Miss}, 轮次: {Turns}",
                        meta.CacheHitTokens, meta.CacheMissTokens, meta.TurnCount);
                }

                await TryColdResumePruneAsync(meta, cancellationToken).ConfigureAwait(false);
            }
        } catch (Exception ex) {
            _logger.LogError(ex, "加载聊天上下文时出错");
            span?.SetStatus(TelemetryStatusCode.Error, ex.Message);
            span?.RecordException(ex);
            throw;
        }
    }

    /// <summary>
    /// 冷恢复剪裁 — 会话空闲超 vendor 缓存 TTL 时服务端缓存已冷，重写前缀零额外
    /// miss 成本，此时剪裁过期大工具结果给全价首请求瘦身。
    /// 对齐 Reasonix Go 版 maybeColdResumePrune：meta 无时间戳保守跳过、缓存仍热跳过、
    /// 剪裁有结果才持久化（保存文件与提示词同步）。
    /// </summary>
    private async Task TryColdResumePruneAsync(SessionMeta? meta, CancellationToken cancellationToken) {
        if (meta is null || meta.UpdatedAtUtcTicks <= 0) {
            return;
        }

        var idle = _clock.GetUtcNow().Ticks - meta.UpdatedAtUtcTicks;
        var ttl = CacheTtlResolver.DefaultCacheTtl(_providerBaseUrl);
        if (idle < ttl.Ticks) {
            return;
        }

        SnipStats snip;

        snip = ContextFoldDecider.SnipStaleToolResults(
            Log,
            _contextWindowResolver.ResolveCurrentContextWindow(),
            _thresholds);

        if (snip.Results == 0) {
            return;
        }

        _logger.LogInformation(
            "会话空闲 {Idle} 超缓存 TTL {Ttl}，冷恢复剪裁 {Results} 条过期工具结果，节省约 {SavedChars} 字符",
            TimeSpan.FromTicks(idle).TotalHours.ToString("F1") + "h",
            ttl.ToString(),
            snip.Results,
            snip.SavedChars);

        _telemetryService?.RecordCount("context.cold_resume_snip.count",
            new() { ["results"] = snip.Results.ToString() },
            "count", "Cold resume snip operation count");
        _telemetryService?.RecordHistogram("context.cold_resume_snip.saved_chars", snip.SavedChars,
            unit: "chars", description: "Chars saved by cold resume snip");

        await SaveContextCoreAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加用户消息到对话日志
    /// </summary>
    public async Task AddUserMessageAsync(string content, MessageOriginKind? originKind = null, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new AddUserMessageCmd(content, originKind, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加用户消息内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task AddUserMessageInternalAsync(string content, MessageOriginKind? originKind, CancellationToken cancellationToken) {
        var metadata = originKind is null
            ? null
            : new Dictionary<string, JsonElement> {
                [MessageMetadataKeyEnumConstants.Origin] = JsonElementHelper.FromJson($"{{\"kind\":\"{originKind.Value.ToValue()}\"}}")
            };
        Log.Append(new ApiMessage(MessageRole.User, content, metadata));
        _logger.LogDebug("已添加用户消息，当前对话数: {Count}", Log.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加压缩摘要消息到对话日志（标记 isCompactSummary 元数据）
    /// </summary>
    public async Task AddCompactSummaryMessageAsync(string content, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new AddCompactSummaryCmd(content, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加压缩摘要消息内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task AddCompactSummaryInternalAsync(string content, CancellationToken cancellationToken) {
        Log.Append(new ApiMessage(MessageRole.User, content, new Dictionary<string, JsonElement> {
            ["isCompactSummary"] = JsonElementHelper.FromBoolean(true)
        }));
        _logger.LogDebug("已添加压缩摘要消息，当前对话数: {Count}", Log.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加助手消息到对话日志
    /// </summary>
    public async Task AddAssistantMessageAsync(string content, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new AddAssistantMessageCmd(content, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加助手消息内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task AddAssistantMessageInternalAsync(string content, CancellationToken cancellationToken) {
        Log.Append(new ApiMessage(MessageRole.Assistant, content));
        _logger.LogDebug("已添加助手消息，当前对话数: {Count}", Log.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加助手工具调用消息（含元数据）到对话日志
    /// </summary>
    public async Task AddAssistantToolCallMessageAsync(string? content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new AddAssistantToolCallCmd(content, metadata, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加助手工具调用消息内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task AddAssistantToolCallInternalAsync(string? content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken) {
        Log.Append(new ApiMessage(MessageRole.Assistant, content, metadata));
        _logger.LogDebug("已添加助手工具调用消息，当前对话数: {Count}", Log.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加工具结果消息到对话日志
    /// </summary>
    public async Task AddToolResultMessageAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new AddToolResultCmd(content, metadata, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加工具结果消息内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task AddToolResultInternalAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, CancellationToken cancellationToken) {
        Log.Append(new ApiMessage(MessageRole.Tool, content, metadata));
        _logger.LogDebug("已添加工具结果消息，当前对话数: {Count}", Log.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加包含多模态内容的工具结果消息 — 对齐 TS BashTool image output
    /// </summary>
    public async Task AddToolResultMessageAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, IReadOnlyList<ToolContent>? contentBlocks, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new AddToolResultWithBlocksCmd(content, metadata, contentBlocks, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加含多模态内容的工具结果消息内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task AddToolResultWithBlocksInternalAsync(string content, IReadOnlyDictionary<string, JsonElement> metadata, IReadOnlyList<ToolContent>? contentBlocks, CancellationToken cancellationToken) {
        Log.Append(new ApiMessage(MessageRole.Tool, content, metadata) { ContentBlocks = contentBlocks ?? [] });
        _logger.LogDebug("已添加工具结果消息(含多模态)，当前对话数: {Count}", Log.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加系统消息到对话日志
    /// </summary>
    public async Task AddSystemMessageAsync(string content, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new AddSystemMessageCmd(content, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加系统消息内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task AddSystemMessageInternalAsync(string content, CancellationToken cancellationToken) {
        Log.Append(new ApiMessage(MessageRole.System, content));
        _logger.LogDebug("已添加系统消息，当前对话数: {Count}", Log.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 添加动态系统消息，该消息独立于对话日志，会随前缀一起组装
    /// </summary>
    public async Task AddDynamicSystemMessageAsync(string content, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(content);

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new AddDynamicSystemMessageCmd(content, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 添加动态系统消息内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task AddDynamicSystemMessageInternalAsync(string content, CancellationToken cancellationToken) {
        _promptStore.AddDynamic(content);
        _logger.LogDebug("已添加动态系统消息，当前动态消息数: {Count}", _promptStore.GetDynamicMessages().Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 清空所有动态系统消息
    /// </summary>
    public async Task ClearDynamicSystemMessagesAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new ClearDynamicSystemMessagesCmd(reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 清空动态系统消息内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task ClearDynamicSystemMessagesInternalAsync(CancellationToken cancellationToken) {
        _promptStore.ClearDynamic();
        _logger.LogDebug("已清空动态系统消息");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 清空所有对话消息和动态系统消息，保留静态系统提示词
    /// </summary>
    public async Task ClearMessagesAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new ClearMessagesCmd(reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 清空对话消息内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task ClearMessagesInternalAsync(CancellationToken cancellationToken) {
        Log.CompactInPlace([]);
        _promptStore.ResetCache();
        _logger.LogInformation("聊天消息已清空，保留静态系统提示词");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 更新静态系统提示词，清空缓存
    /// </summary>
    public async Task UpdateSystemPromptAsync(string systemPrompt, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(systemPrompt);

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new UpdateSystemPromptCmd(systemPrompt, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新静态系统提示词内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task UpdateSystemPromptInternalAsync(string systemPrompt, CancellationToken cancellationToken) {
        _promptStore.Update(systemPrompt);
        _logger.LogInformation("静态系统提示词已更新，长度: {Len}", _promptStore.StaticPrompt.Length);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取组装后的完整消息列表（静态系统提示词 + 动态系统消息 + 对话日志）
    /// </summary>
    public async Task<MessageList> GetMessageListAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<MessageList>();
        await _actor.SendAsync(new GetMessageListCmd(reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取消息列表内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task<MessageList> GetMessageListInternalAsync(CancellationToken cancellationToken) {
        var messages = AssembleMessages();
        return Task.FromResult(MessageList.FromList(messages));
    }

    /// <summary>
    /// 将当前聊天上下文持久化保存，包括系统提示词、对话历史和会话统计
    /// </summary>
    public async Task SaveContextAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new SaveContextCmd(reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 保存上下文内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private async Task SaveContextInternalAsync(CancellationToken cancellationToken) {
        await using var span = _telemetryService?.StartSpan("context.save", TelemetrySpanKind.Server);
        try {
            await SaveContextCoreAsync(cancellationToken).ConfigureAwait(false);
            span?.SetStatus(TelemetryStatusCode.Ok);
        } catch (Exception ex) {
            _logger.LogError(ex, "保存聊天上下文时出错");
            span?.SetStatus(TelemetryStatusCode.Error, ex.Message);
            span?.RecordException(ex);
            throw;
        }
    }

    /// <summary>
    /// 保存核心逻辑（由 Actor Consumer 串行调用，无显式锁）
    /// </summary>
    private async Task SaveContextCoreAsync(CancellationToken cancellationToken) {
        var staticPrefix = _promptStore.StaticPrompt;
        var conversationSnapshot = new MessageList(Log.ToMessages());

        await _stateService.SaveStateAsync(staticPrefix, conversationSnapshot, cancellationToken).ConfigureAwait(false);

        if (_metaStore is not null && _sessionStats is not null) {
            var meta = _sessionStats.ToMeta(updatedAtUtcTicks: _clock.GetUtcNow().Ticks);
            await _metaStore.SaveAsync(_sessionId, meta, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug("聊天上下文已保存");
    }

    /// <summary>
    /// 根据本次 token 用量决定是否需要折叠上下文
    /// 缓存命中且低于硬阈值时返回 <see cref="ContextFoldDecision.Deferred"/>，推迟折叠以保留缓存前缀
    /// </summary>
    public ContextFoldDecision DecideAfterUsage(TokenUsage usage, bool alreadyFoldedThisTurn = false) {
        var decision = ContextFoldDecider.DecideAfterUsage(
            usage,
            _contextWindowResolver.ResolveCurrentContextWindow(),
            alreadyFoldedThisTurn,
            _thresholds,
            _deferredFoldCount);

        if (decision == ContextFoldDecision.Deferred) {
            _deferredFoldCount++;
            _logger?.LogInformation("缓存命中，上下文折叠推迟（第 {DeferralCount}/{DeferFoldLimit} 次），保留缓存前缀", _deferredFoldCount, _thresholds.DeferFoldLimit);
            return decision;
        }

        _deferredFoldCount = 0;

        if (decision is ContextFoldDecision.FoldNormal or ContextFoldDecision.FoldAggressive
            && ContextFoldDecider.IsFoldStuck(_consecutiveNoProgressFolds, _thresholds.StuckFoldLimit)) {
            _logger?.LogWarning("上下文折叠连续 {Count} 次无进展（窗口过小），暂停自动折叠以避免每轮重试", _consecutiveNoProgressFolds);
            return ContextFoldDecision.None;
        }

        return decision;
    }

    /// <summary>
    /// 在发送请求前预判是否需要折叠，基于当前消息和工具规格估算 token 占用
    /// </summary>
    public PreflightDecision DecidePreflight(IReadOnlyList<ToolSpec> toolSpecs) {
        var messages = AssembleMessages();
        return ContextFoldDecider.DecidePreflight(messages, toolSpecs, _contextWindowResolver.ResolveCurrentContextWindow(), _thresholds);
    }

    /// <summary>
    /// 根据折叠决策执行上下文折叠操作（普通/激进/摘要退出）
    /// </summary>
    public async Task<ContextFoldResult> FoldIfNeededAsync(ContextFoldDecision decision, string? agentId = null, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<ContextFoldResult>();
        await _actor.SendAsync(new FoldIfNeededCmd(decision, agentId, reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 折叠上下文内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private async Task<ContextFoldResult> FoldIfNeededInternalAsync(ContextFoldDecision decision, string? agentId, CancellationToken cancellationToken) {
        if (_foldExecutor == null) {
            if (decision is ContextFoldDecision.FoldNormal or ContextFoldDecision.FoldAggressive) {
                _consecutiveNoProgressFolds++;
            }

            return new ContextFoldResult {
                Folded = false,
                Decision = decision,
                OriginalMessageCount = Log.Count
            };
        }

        await using var foldSpan = _telemetryService?.StartSpan("context.fold", TelemetrySpanKind.Server);
        foldSpan?.SetTag("context.fold_decision", decision.ToString());

        try {
            // 折叠前先做低成本剪裁：过期的大工具结果可重派生，重写它们无需调用摘要器。
            // 若剪裁本身已把前缀压回阈值以下，则跳过本轮昂贵的摘要折叠（对齐 Reasonix Go 版
            // maybeCompact 的 prune-before-fold：裁剪省一轮 summarize）。
            var snip = ContextFoldDecider.SnipStaleToolResults(
                Log,
                _contextWindowResolver.ResolveCurrentContextWindow(),
                _thresholds);

            if (snip.Results > 0) {
                _logger?.LogInformation("折叠前剪裁 {Results} 条过期工具结果，节省约 {SavedChars} 字符",
                    snip.Results, snip.SavedChars);

                _telemetryService?.RecordCount("context.snip.count",
                    new() { ["results"] = snip.Results.ToString() },
                    "count", "Context fold pre-snip operation count");
                _telemetryService?.RecordHistogram("context.snip.saved_chars", snip.SavedChars,
                    unit: "chars", description: "Chars saved by context fold pre-snip");

                var postSnipRatio = (double)ContextFoldDecider.EstimateTokenCount(
                    Log.ToMessages(), _currentToolSpecs, _thresholds)
                    / _contextWindowResolver.ResolveCurrentContextWindow();

                var clearedBySnip = decision switch {
                    ContextFoldDecision.FoldNormal => postSnipRatio <= _thresholds.FoldThreshold,
                    ContextFoldDecision.FoldAggressive => postSnipRatio <= _thresholds.AggressiveThreshold,
                    _ => false
                };

                if (clearedBySnip) {
                    _consecutiveNoProgressFolds = 0;
                    GetCacheBreakDetector(agentId).NotifyCompaction();

                    return new ContextFoldResult {
                        Folded = false,
                        Decision = decision,
                        Snip = snip,
                        OriginalMessageCount = Log.Count
                    };
                }
            }

            var foldResult = decision switch {
                ContextFoldDecision.FoldNormal => await _foldExecutor.FoldAsync(Log, _contextWindowResolver.ResolveCurrentContextWindow(), aggressive: false, _thresholds, cancellationToken).ConfigureAwait(false),
                ContextFoldDecision.FoldAggressive => await _foldExecutor.FoldAsync(Log, _contextWindowResolver.ResolveCurrentContextWindow(), aggressive: true, _thresholds, cancellationToken).ConfigureAwait(false),
                ContextFoldDecision.ExitWithSummary => _foldExecutor.TrimTrailingAndPrepareExit(Log),
                _ => new ContextFoldResult { Folded = false, Decision = decision, OriginalMessageCount = Log.Count }
            };

            if (snip.Results > 0) {
                foldResult = new ContextFoldResult {
                    Folded = foldResult.Folded,
                    HeadMessageCount = foldResult.HeadMessageCount,
                    TailMessageCount = foldResult.TailMessageCount,
                    OriginalMessageCount = foldResult.OriginalMessageCount,
                    Summary = foldResult.Summary,
                    Decision = foldResult.Decision,
                    Snip = snip
                };
            }

            // 折叠/压缩确实改写前缀后，通知检测器重置缓存基线，避免下一次 miss 被误报为驱逐
            if (foldResult.Folded) {
                _consecutiveNoProgressFolds = 0;
                GetCacheBreakDetector(agentId).NotifyCompaction();
            } else if (decision is ContextFoldDecision.FoldNormal or ContextFoldDecision.FoldAggressive
                       && snip.Results == 0) {
                // 折叠动作执行但未产生任何缩减（窗口过小），累计无进展次数，触发卡死守卫
                _consecutiveNoProgressFolds++;
            }

            // L5 兜底：折叠/剪裁后仍超 EmergencyThreshold → 抛 ContextOverflowException
            if ((foldResult.Folded || snip.Results > 0) && decision is not ContextFoldDecision.None) {
                var postFoldTokens = ContextFoldDecider.EstimateTokenCount(
                    Log.ToMessages(), _currentToolSpecs, _thresholds);
                var ctxMax = _contextWindowResolver.ResolveCurrentContextWindow();
                if (postFoldTokens > ctxMax * _thresholds.EmergencyThreshold) {
                    _logger?.LogError("上下文溢出：折叠后 {Tokens} token 仍超过紧急阈值 {Threshold} token（ctxMax={CtxMax}）",
                        postFoldTokens, (int)(ctxMax * _thresholds.EmergencyThreshold), ctxMax);
                    throw new ContextOverflowException(
                        $"上下文溢出：折叠后 {postFoldTokens} token 仍超过紧急阈值 {(int)(ctxMax * _thresholds.EmergencyThreshold)} token",
                        ctxMax, postFoldTokens);
                }
            }

            return foldResult;
        } finally {
            foldSpan?.SetStatus(TelemetryStatusCode.Ok);
            _telemetryService?.RecordCount("context.fold.count", new() { ["decision"] = decision.ToString() }, "count", "Context fold count");
        }
    }

    /// <summary>
    /// 获取当前上下文窗口的最大 token 数
    /// </summary>
    public int GetContextMaxTokens() => _contextWindowResolver.ResolveCurrentContextWindow();

    /// <summary>
    /// 撤回最后一轮对话（SP-3），移除最近的用户-助手消息对
    /// </summary>
    public async Task<RewindResult> RewindLastTurnAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<RewindResult>();
        await _actor.SendAsync(new RewindLastTurnCmd(reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 撤回最后一轮对话内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task<RewindResult> RewindLastTurnInternalAsync(CancellationToken cancellationToken) {
        var removed = Log.TrimLastTurn();
        _logger.LogInformation("撤回最后一轮对话 (SP-3)，移除 {Count} 条消息，剩余 {Remaining} 条",
            removed, Log.Count);

        _telemetryService?.RecordCount("context.rewind.count", new() { ["kind"] = "last_turn" }, "count", "Context rewind count");

        return Task.FromResult(RewindResult.Ok(RewindKind.TrimLastTurn, removed, Log.Count));
    }

    /// <summary>
    /// 撤回到指定消息索引（SP-5），移除该索引之后的所有消息
    /// </summary>
    public async Task<RewindResult> RewindToMessageIndexAsync(int messageIndex, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<RewindResult>();
        await _actor.SendAsync(new RewindToMessageIndexCmd(messageIndex, reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 撤回到指定消息索引内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task<RewindResult> RewindToMessageIndexInternalAsync(int messageIndex, CancellationToken cancellationToken) {
        if (messageIndex < 0 || messageIndex > Log.Count) {
            return Task.FromResult(RewindResult.Fail(
                $"消息索引 {messageIndex} 超出范围 [0, {Log.Count}]"));
        }

        var removed = Log.TruncateTo(messageIndex);
        _logger.LogInformation("撤回到消息索引 {Index} (SP-5)，移除 {Count} 条消息，剩余 {Remaining} 条",
            messageIndex, removed, Log.Count);

        return Task.FromResult(RewindResult.Ok(RewindKind.TruncateToIndex, removed, Log.Count));
    }

    /// <summary>
    /// 撤回到会话初始状态（SP-0），清空所有对话消息和动态系统消息
    /// </summary>
    public async Task<RewindResult> RewindToStartAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<RewindResult>();
        await _actor.SendAsync(new RewindToStartCmd(reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 撤回到会话初始状态内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task<RewindResult> RewindToStartInternalAsync(CancellationToken cancellationToken) {
        var removed = Log.Count;
        Log.CompactInPlace([]);
        _promptStore.ResetCache();

        _logger.LogInformation("撤回到会话初始状态 (SP-0)，移除 {Count} 条消息，前缀保留", removed);

        return Task.FromResult(RewindResult.Ok(RewindKind.ClearHistory, removed, 0));
    }

    /// <summary>
    /// 更新当前可用的工具规格列表，同时识别并记录 MCP 延迟工具
    /// </summary>
    public async Task UpdateToolSpecsAsync(IReadOnlyList<ToolSpec> toolSpecs, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(toolSpecs);

        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new UpdateToolSpecsCmd(toolSpecs, reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 更新工具规格内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task UpdateToolSpecsInternalAsync(IReadOnlyList<ToolSpec> toolSpecs, CancellationToken cancellationToken) {
        _currentToolSpecs.Clear();
        _currentToolSpecs.AddRange(toolSpecs);

        _deferredTools.Clear();
        foreach (var spec in toolSpecs) {
            var isMcp = spec.Name.Contains('.');
            if (isMcp) {
                _deferredTools.Add(new DeferredToolInfo(spec.Name, spec.Description, spec.InputSchemaJson, isMcp: true, spec.Category, spec.GroupName));
            }
        }

        _logger.LogDebug("工具规格已更新，当前 {Count} 个工具，{DeferredCount} 个延迟工具",
            _currentToolSpecs.Count, _deferredTools.Count);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 记录当前提示词前缀状态快照，用于后续缓存失效检测
    /// </summary>
    public async Task<PromptStateSnapshot> RecordPromptStateAsync(string? agentId = null, CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource<PromptStateSnapshot>();
        await _actor.SendAsync(new RecordPromptStateCmd(agentId, reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 记录提示词前缀状态快照内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task<PromptStateSnapshot> RecordPromptStateInternalAsync(string? agentId, CancellationToken cancellationToken) {
        var prefix = new ImmutablePrefix(_promptStore.StaticPrompt, _currentToolSpecs, []);
        var dynamicContent = _promptStore.GetDynamicContent();
        var snapshot = GetCacheBreakDetector(agentId).RecordPromptState(prefix, dynamicContent, Log.ToMessages());

        var toolSpecsBytes = _currentToolSpecs.Sum(t =>
            System.Text.Encoding.UTF8.GetByteCount(t.Name) +
            (t.Description != null ? System.Text.Encoding.UTF8.GetByteCount(t.Description) : 0) +
            (t.InputSchemaJson != null ? System.Text.Encoding.UTF8.GetByteCount(t.InputSchemaJson) : 0));
        var systemBytes = System.Text.Encoding.UTF8.GetByteCount(_promptStore.StaticPrompt);
        var estimatedTokens = ContextFoldDecider.EstimateTokenCount(
            [new ApiMessage(MessageRole.System, _promptStore.StaticPrompt)],
            _currentToolSpecs);

        _logger.LogInformation(
            "前缀状态快照已记录，SystemHash={SystemHash}, SystemBytes={SystemBytes}, ToolCount={ToolCount}, ToolNamesHash={ToolNamesHash}, ToolSpecsBytes={ToolSpecsBytes}, EstimatedTokens={EstimatedTokens}",
            snapshot.SystemPromptHash, systemBytes, snapshot.ToolCount, snapshot.ToolNamesHash, toolSpecsBytes, estimatedTokens);

        return Task.FromResult(snapshot);
    }

    /// <summary>
    /// 检测缓存是否失效，对比快照与当前前缀状态并结合 token 用量判断
    /// </summary>
    public async Task<CacheBreakResult> CheckCacheBreakAsync(PromptStateSnapshot snapshot, TokenUsage usage, string? agentId = null, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(usage);

        var reply = new TaskCompletionSource<CacheBreakResult>();
        await _actor.SendAsync(new CheckCacheBreakCmd(snapshot, usage, agentId, reply), cancellationToken).ConfigureAwait(false);
        return await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 检测缓存失效内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private Task<CacheBreakResult> CheckCacheBreakInternalAsync(PromptStateSnapshot snapshot, TokenUsage usage, string? agentId, CancellationToken cancellationToken) {
        var currentPrefix = new ImmutablePrefix(_promptStore.StaticPrompt, _currentToolSpecs, []);
        var currentDynamicContent = _promptStore.GetDynamicContent();
        var result = GetCacheBreakDetector(agentId).CheckCacheBreak(snapshot, currentPrefix, currentDynamicContent, usage, Log.ToMessages());

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
    /// 获取已发现的工具集合
    /// </summary>
    public DiscoveredToolSet GetDiscoveredTools() {
        return _discoveredTools;
    }

    /// <summary>
    /// 获取延迟加载的工具信息列表（主要是 MCP 工具）
    /// </summary>
    public IEnumerable<DeferredToolInfo> GetDeferredTools() {
        return _deferredTools;
    }

    /// <summary>
    /// 从对话历史中提取已发现的工具名称并同步到已发现工具集合
    /// </summary>
    public async Task SyncDiscoveredToolsFromHistoryAsync(CancellationToken cancellationToken = default) {
        var reply = new TaskCompletionSource();
        await _actor.SendAsync(new SyncDiscoveredToolsFromHistoryCmd(reply), cancellationToken).ConfigureAwait(false);
        await _actor.AskReplyAsync(reply, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 从历史同步已发现工具内部实现 — 由 Actor Consumer 串行调用，无显式锁
    /// </summary>
    private async Task SyncDiscoveredToolsFromHistoryInternalAsync(CancellationToken cancellationToken) {
        var history = AssembleMessages();
        var chatHistory = MessageList.FromList(history);
        var discovered = ToolReferenceExtractor.ExtractDiscoveredToolNames(chatHistory);
        await _discoveredTools.DiscoverRangeAsync(discovered).ConfigureAwait(false);
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

        foreach (var msg in Log.ToMessages()) {
            messages.Add(msg);
        }

        return messages;
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

        protected override async ValueTask HandleAsync(ChatContextCommand cmd, CancellationToken ct) {
            try {
                switch (cmd) {
                    case LoadContextCmd(var reply):
                    await _owner.LoadContextInternalAsync(ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case AddUserMessageCmd(var content, var originKind, var reply):
                    await _owner.AddUserMessageInternalAsync(content, originKind, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case AddCompactSummaryCmd(var content, var reply):
                    await _owner.AddCompactSummaryInternalAsync(content, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case AddAssistantMessageCmd(var content, var reply):
                    await _owner.AddAssistantMessageInternalAsync(content, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case AddAssistantToolCallCmd(var content, var metadata, var reply):
                    await _owner.AddAssistantToolCallInternalAsync(content, metadata, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case AddToolResultCmd(var content, var metadata, var reply):
                    await _owner.AddToolResultInternalAsync(content, metadata, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case AddToolResultWithBlocksCmd(var content, var metadata, var contentBlocks, var reply):
                    await _owner.AddToolResultWithBlocksInternalAsync(content, metadata, contentBlocks, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case AddSystemMessageCmd(var content, var reply):
                    await _owner.AddSystemMessageInternalAsync(content, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case AddDynamicSystemMessageCmd(var content, var reply):
                    await _owner.AddDynamicSystemMessageInternalAsync(content, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case ClearDynamicSystemMessagesCmd(var reply):
                    await _owner.ClearDynamicSystemMessagesInternalAsync(ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case ClearMessagesCmd(var reply):
                    await _owner.ClearMessagesInternalAsync(ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case UpdateSystemPromptCmd(var systemPrompt, var reply):
                    await _owner.UpdateSystemPromptInternalAsync(systemPrompt, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case GetMessageListCmd(var reply):
                    reply.SetResult(await _owner.GetMessageListInternalAsync(ct).ConfigureAwait(false));
                    break;
                    case SaveContextCmd(var reply):
                    await _owner.SaveContextInternalAsync(ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case FoldIfNeededCmd(var decision, var agentId, var reply):
                    reply.SetResult(await _owner.FoldIfNeededInternalAsync(decision, agentId, ct).ConfigureAwait(false));
                    break;
                    case RewindLastTurnCmd(var reply):
                    reply.SetResult(await _owner.RewindLastTurnInternalAsync(ct).ConfigureAwait(false));
                    break;
                    case RewindToMessageIndexCmd(var messageIndex, var reply):
                    reply.SetResult(await _owner.RewindToMessageIndexInternalAsync(messageIndex, ct).ConfigureAwait(false));
                    break;
                    case RewindToStartCmd(var reply):
                    reply.SetResult(await _owner.RewindToStartInternalAsync(ct).ConfigureAwait(false));
                    break;
                    case UpdateToolSpecsCmd(var toolSpecs, var reply):
                    await _owner.UpdateToolSpecsInternalAsync(toolSpecs, ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                    case RecordPromptStateCmd(var agentId, var reply):
                    reply.SetResult(await _owner.RecordPromptStateInternalAsync(agentId, ct).ConfigureAwait(false));
                    break;
                    case CheckCacheBreakCmd(var snapshot, var usage, var agentId, var reply):
                    reply.SetResult(await _owner.CheckCacheBreakInternalAsync(snapshot, usage, agentId, ct).ConfigureAwait(false));
                    break;
                    case SyncDiscoveredToolsFromHistoryCmd(var reply):
                    await _owner.SyncDiscoveredToolsFromHistoryInternalAsync(ct).ConfigureAwait(false);
                    reply.SetResult();
                    break;
                }
            } catch (OperationCanceledException) { throw; } catch (Exception ex) { cmd.SetReplyException(ex); }
        }

        protected override void OnConsumerError(Exception ex)
            => _logger.LogWarning(ex, "ChatContextActor 命令处理异常");
    }
}