namespace Core.Context;

/// <summary>
/// 上下文折叠服务实现 — 管理折叠决策与执行
/// <para>单一数据源: _deferredFoldCount + _consecutiveNoProgressFolds（折叠状态）</para>
/// <para>依赖: ISessionStore, IToolSpecStore, ICacheBreakStore, IContextWindowResolver, ContextFoldExecutor?</para>
/// <para>由 ChatContextManager Actor Consumer 串行调用，方法无需线程安全。</para>
/// </summary>
internal sealed class ContextFoldService : IContextFoldService {
    private readonly ISessionStore _sessionStore;
    private readonly IToolSpecStore _toolSpecStore;
    private readonly ICacheBreakStore _cacheBreakStore;
    private readonly IContextWindowResolver _contextWindowResolver;
    private readonly ContextFoldExecutor? _foldExecutor;
    private readonly ContextFoldThresholds _thresholds;
    private readonly ITelemetryService? _telemetryService;
    private readonly ILogger? _logger;

    private int _deferredFoldCount;
    private int _consecutiveNoProgressFolds;

    /// <summary>构造上下文折叠服务</summary>
    public ContextFoldService(
        ISessionStore sessionStore,
        IToolSpecStore toolSpecStore,
        ICacheBreakStore cacheBreakStore,
        IContextWindowResolver contextWindowResolver,
        ContextFoldExecutor? foldExecutor,
        ContextFoldThresholds thresholds,
        ITelemetryService? telemetryService,
        ILogger? logger) {
        _sessionStore = sessionStore;
        _toolSpecStore = toolSpecStore;
        _cacheBreakStore = cacheBreakStore;
        _contextWindowResolver = contextWindowResolver;
        _foldExecutor = foldExecutor;
        _thresholds = thresholds;
        _telemetryService = telemetryService;
        _logger = logger;
    }

    /// <inheritdoc />
    public ContextFoldDecision DecideAfterUsage(TokenUsage usage, bool alreadyFoldedThisTurn) {
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

    /// <inheritdoc />
    public PreflightDecision DecidePreflight(IReadOnlyList<ToolSpec> toolSpecs, IReadOnlyList<ApiMessage> messages) {
        return ContextFoldDecider.DecidePreflight(messages, toolSpecs, _contextWindowResolver.ResolveCurrentContextWindow(), _thresholds);
    }

    /// <inheritdoc />
    public async Task<ContextFoldResult> FoldIfNeededAsync(
        ContextFoldDecision decision,
        string? agentId,
        CancellationToken cancellationToken) {
        if (_foldExecutor == null) {
            if (decision is ContextFoldDecision.FoldNormal or ContextFoldDecision.FoldAggressive) {
                _consecutiveNoProgressFolds++;
            }

            return new ContextFoldResult {
                Folded = false,
                Decision = decision,
                OriginalMessageCount = _sessionStore.Count
            };
        }

        await using var foldSpan = _telemetryService?.StartSpan("context.fold", TelemetrySpanKind.Server);
        foldSpan?.SetTag("context.fold_decision", decision.ToString());

        try {
            var snip = ContextFoldDecider.SnipStaleToolResults(
                _sessionStore.Log,
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
                    _sessionStore.ToMessages(), _toolSpecStore.CurrentSpecs, _thresholds)
                    / _contextWindowResolver.ResolveCurrentContextWindow();

                var clearedBySnip = decision switch {
                    ContextFoldDecision.FoldNormal => postSnipRatio <= _thresholds.FoldThreshold,
                    ContextFoldDecision.FoldAggressive => postSnipRatio <= _thresholds.AggressiveThreshold,
                    _ => false
                };

                if (clearedBySnip) {
                    _consecutiveNoProgressFolds = 0;
                    _cacheBreakStore.NotifyCompaction(agentId);

                    return new ContextFoldResult {
                        Folded = false,
                        Decision = decision,
                        Snip = snip,
                        OriginalMessageCount = _sessionStore.Count
                    };
                }
            }

            var foldResult = decision switch {
                ContextFoldDecision.FoldNormal => await _foldExecutor.FoldAsync(_sessionStore.Log, _contextWindowResolver.ResolveCurrentContextWindow(), aggressive: false, _thresholds, cancellationToken).ConfigureAwait(false),
                ContextFoldDecision.FoldAggressive => await _foldExecutor.FoldAsync(_sessionStore.Log, _contextWindowResolver.ResolveCurrentContextWindow(), aggressive: true, _thresholds, cancellationToken).ConfigureAwait(false),
                ContextFoldDecision.ExitWithSummary => _foldExecutor.TrimTrailingAndPrepareExit(_sessionStore.Log),
                _ => new ContextFoldResult { Folded = false, Decision = decision, OriginalMessageCount = _sessionStore.Count }
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

            if (foldResult.Folded) {
                _consecutiveNoProgressFolds = 0;
                _cacheBreakStore.NotifyCompaction(agentId);
            } else if (decision is ContextFoldDecision.FoldNormal or ContextFoldDecision.FoldAggressive
                       && snip.Results == 0) {
                _consecutiveNoProgressFolds++;
            }

            if ((foldResult.Folded || snip.Results > 0) && decision is not ContextFoldDecision.None) {
                var postFoldTokens = ContextFoldDecider.EstimateTokenCount(
                    _sessionStore.ToMessages(), _toolSpecStore.CurrentSpecs, _thresholds);
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
}
