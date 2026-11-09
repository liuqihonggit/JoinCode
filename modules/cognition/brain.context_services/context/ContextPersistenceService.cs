namespace Core.Context;

/// <summary>
/// 上下文持久化服务实现 — 管理上下文的加载和保存
/// <para>单一数据源: IStateService + ISessionMetaStore（持久化存储）</para>
/// <para>依赖: ISessionStore, IPromptStore, SessionStats?</para>
/// <para>由 ChatContextManager Actor Consumer 串行调用，方法无需线程安全。</para>
/// </summary>
internal sealed class ContextPersistenceService : IContextPersistenceService {
    private readonly ISessionStore _sessionStore;
    private readonly IPromptStore _promptStore;
    private readonly IStateService _stateService;
    private readonly ISessionMetaStore? _metaStore;
    private readonly SessionStats? _sessionStats;
    private readonly IContextWindowResolver _contextWindowResolver;
    private readonly ContextFoldThresholds _thresholds;
    private readonly ITelemetryService? _telemetryService;
    private readonly IClockService _clock;
    private readonly string? _providerBaseUrl;
    private readonly ILogger _logger;

    /// <summary>构造上下文持久化服务</summary>
    public ContextPersistenceService(
        ISessionStore sessionStore,
        IPromptStore promptStore,
        IStateService stateService,
        ISessionMetaStore? metaStore,
        SessionStats? sessionStats,
        IContextWindowResolver contextWindowResolver,
        ContextFoldThresholds thresholds,
        ITelemetryService? telemetryService,
        IClockService clock,
        string? providerBaseUrl,
        ILogger logger) {
        _sessionStore = sessionStore;
        _promptStore = promptStore;
        _stateService = stateService;
        _metaStore = metaStore;
        _sessionStats = sessionStats;
        _contextWindowResolver = contextWindowResolver;
        _thresholds = thresholds;
        _telemetryService = telemetryService;
        _clock = clock;
        _providerBaseUrl = providerBaseUrl;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task LoadAsync(string sessionId, CancellationToken cancellationToken) {
        await using var span = _telemetryService?.StartSpan("context.load", TelemetrySpanKind.Server);
        try {
            var (systemPrompt, chatHistory) = await _stateService.LoadStateAsync(cancellationToken).ConfigureAwait(false);

            _promptStore.Update(systemPrompt ?? string.Empty);
            _sessionStore.CompactInPlace([]);

            if (chatHistory is { Count: > 0 }) {
                foreach (var msg in chatHistory) {
                    if (msg.Role != MessageRole.System) {
                        _sessionStore.Append(new ApiMessage(msg.Role, msg.Content, msg.Metadata));
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(_promptStore.StaticPrompt)) {
                        _promptStore.Update(msg.Content ?? string.Empty);
                    }
                    continue;
                }
            }

            _logger.LogInformation("聊天上下文已加载，静态前缀长度: {Len}, 对话消息数: {Count}",
                _promptStore.StaticPrompt.Length, _sessionStore.Count);

            span?.SetTag("context.message_count", _sessionStore.Count);
            span?.SetStatus(TelemetryStatusCode.Ok);

            if (_metaStore is not null && _sessionStats is not null) {
                var meta = await _metaStore.LoadAsync(sessionId, cancellationToken).ConfigureAwait(false);
                if (meta is not null) {
                    _sessionStats.SeedCarryover(meta.CacheHitTokens, meta.CacheMissTokens, meta.TotalCostUsd);
                    _logger.LogInformation("会话统计已恢复，缓存命中: {Hit}, 未命中: {Miss}, 轮次: {Turns}",
                        meta.CacheHitTokens, meta.CacheMissTokens, meta.TurnCount);
                }

                await TryColdResumePruneAsync(meta, sessionId, cancellationToken).ConfigureAwait(false);
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
    /// </summary>
    private async Task TryColdResumePruneAsync(SessionMeta? meta, string sessionId, CancellationToken cancellationToken) {
        if (meta is null || meta.UpdatedAtUtcTicks <= 0) {
            return;
        }

        var idle = _clock.GetUtcNow().Ticks - meta.UpdatedAtUtcTicks;
        var ttl = CacheTtlResolver.DefaultCacheTtl(_providerBaseUrl);
        if (idle < ttl.Ticks) {
            return;
        }

        var snip = ContextFoldDecider.SnipStaleToolResults(
            _sessionStore.Log,
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

        await SaveCoreAsync(sessionId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task SaveAsync(string sessionId, CancellationToken cancellationToken) {
        await using var span = _telemetryService?.StartSpan("context.save", TelemetrySpanKind.Server);
        try {
            await SaveCoreAsync(sessionId, cancellationToken).ConfigureAwait(false);
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
    private async Task SaveCoreAsync(string sessionId, CancellationToken cancellationToken) {
        var staticPrefix = _promptStore.StaticPrompt;
        var conversationSnapshot = new MessageList(_sessionStore.ToMessages());

        await _stateService.SaveStateAsync(staticPrefix, conversationSnapshot, cancellationToken).ConfigureAwait(false);

        if (_metaStore is not null && _sessionStats is not null) {
            var meta = _sessionStats.ToMeta(updatedAtUtcTicks: _clock.GetUtcNow().Ticks);
            await _metaStore.SaveAsync(sessionId, meta, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogDebug("聊天上下文已保存");
    }
}
