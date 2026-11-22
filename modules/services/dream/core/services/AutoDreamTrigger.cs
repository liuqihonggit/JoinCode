namespace JoinCode.Dream;

/// <summary>
/// 自动触发结果 — 每个分支都有独立取值，便于真机探针与单测断言「到底是哪道门拦住的」。
/// </summary>
public enum AutoDreamTriggerOutcome {
    /// <summary>已发起整合。</summary>
    Fired,

    /// <summary>自动触发被配置关闭。</summary>
    Disabled,

    /// <summary>自动记忆被配置关闭。</summary>
    MemoryDisabled,

    /// <summary>距上次合并不足 MinHours。</summary>
    TimeGate,

    /// <summary>距上次会话扫描不足 SessionScanIntervalMs。</summary>
    ScanThrottle,

    /// <summary>待处理会话数不足 MinSessions。</summary>
    SessionGate,

    /// <summary>锁被其它持有者占着，或本进程已有一个在跑。</summary>
    LockHeld,

    /// <summary>整合本身失败（已回滚锁，下个回合可重试）。</summary>
    Failed,
}

/// <summary>
/// 自动做梦触发器 — 回合结束时的六个门 + 真正的整合执行（TASK126 / TASK103 §2.3 #40）。
/// <para>
/// 门序严格对齐基准 <c>services/autoDream/autoDream.ts:118-200</c>：配置门 → 时间门 →
/// 扫描节流 → 会话门 → 占锁 → 执行。顺序本身是语义：把占锁提前会让所有回合都去抢文件，
/// 把节流删除会让每个回合都枚举一次会话目录（基准注释：「Per-turn cost when enabled:
/// one GB cache read + one stat」）。
/// </para>
/// <para>
/// <b>为什么不复用 <see cref="DreamFeature"/> 的门控</b>：<c>DreamFeature.ExecuteDirectAsync</c>
/// 的时间门用 <c>UtcNow - MinHours</c> 当「上次合并时刻」（假值，TASK071 已记为待修），
/// 只要 24h 内有 5 个会话，<b>每回合都放行</b>。本类用 <see cref="ConsolidationLock"/> 的真值，
/// 并把会话列表自己算好后以 <c>SessionIds</c> 传入，让 <see cref="DreamFeature"/> 只做执行。
/// </para>
/// <para>
/// <b>失败一律不向调用方传播</b>：这是后台动作，抛异常会污染回合。失败必回滚锁，
/// 使下个满足条件的回合能重试。
/// </para>
/// </summary>
public sealed class AutoDreamTrigger {
    private readonly IDreamFeature _dreamFeature;
    private readonly ISessionScanner _sessionScanner;
    private readonly AutoDreamConfig _config;
    private readonly ConsolidationLock _lock;
    private readonly ILogger<AutoDreamTrigger>? _logger;
    private readonly Func<int, bool>? _isProcessAlive;

    private long _lastScanAtMs;
    private int _running;

    /// <summary>
    /// 构造自动触发器。
    /// </summary>
    /// <param name="dreamFeature">整合执行体（本类只做门控，不重复实现整合）。</param>
    /// <param name="sessionScanner">会话扫描器。</param>
    /// <param name="config">自动做梦配置（MinHours/MinSessions/扫描间隔/开关）。</param>
    /// <param name="lock">整合锁（可选 — 缺省用 PhysicalFileSystem 定位；测试注入 InMemoryFileSystem）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="isProcessAlive">pid 存活判定（测试注入）。</param>
    public AutoDreamTrigger(
        IDreamFeature dreamFeature,
        ISessionScanner sessionScanner,
        AutoDreamConfig config,
        ConsolidationLock? @lock = null,
        ILogger<AutoDreamTrigger>? logger = null,
        Func<int, bool>? isProcessAlive = null) {
        _dreamFeature = dreamFeature ?? throw new ArgumentNullException(nameof(dreamFeature));
        _sessionScanner = sessionScanner ?? throw new ArgumentNullException(nameof(sessionScanner));
        _config = config ?? new AutoDreamConfig();
        _lock = @lock ?? new ConsolidationLock(_config, new IO.FileSystem.PhysicalFileSystem());
        _logger = logger;
        _isProcessAlive = isProcessAlive;
    }

    /// <summary>
    /// 走一遍六门；全过则执行整合。**永不抛异常**（失败返回 <see cref="AutoDreamTriggerOutcome.Failed"/>）。
    /// </summary>
    /// <param name="currentSessionId">当前活跃会话 ID（会话门要排除它 — 它的 mtime 恒为现在，会虚增计数）。</param>
    /// <param name="ct">取消令牌（应用退出时取消；取消也算失败并回滚锁）。</param>
    /// <returns>命中的门或执行结果。</returns>
    public async Task<AutoDreamTriggerOutcome> FireAsync(string? currentSessionId, CancellationToken ct = default) {
        // 进程内互斥放在最前面：省掉后续所有 IO。跨进程靠锁文件（第 5 步）。
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) {
            Diag.WriteLine("[DREAM-AUTO] skip: already-running");
            return AutoDreamTriggerOutcome.LockHeld;
        }

        try {
            var gated = await EvaluateAsync(currentSessionId, ct).ConfigureAwait(false);
            if (gated.Outcome != AutoDreamTriggerOutcome.Fired) {
                return gated.Outcome;
            }

            Diag.WriteLine($"[DREAM-AUTO] fire sessions={gated.SessionIds.Count} prior={gated.PriorMtime}");
            return await ExecuteAsync(gated.SessionIds, gated.PriorMtime, ct).ConfigureAwait(false);
        } finally {
            Volatile.Write(ref _running, 0);
        }
    }

    private async Task<(AutoDreamTriggerOutcome Outcome, IReadOnlyList<string> SessionIds, long PriorMtime)> EvaluateAsync(
        string? currentSessionId,
        CancellationToken ct) {
        if (!_config.Enabled) {
            Diag.WriteLine("[DREAM-AUTO] skip: disabled");
            return (AutoDreamTriggerOutcome.Disabled, [], 0L);
        }

        if (!_config.AutoMemoryEnabled) {
            Diag.WriteLine("[DREAM-AUTO] skip: auto-memory-disabled");
            return (AutoDreamTriggerOutcome.MemoryDisabled, [], 0L);
        }

        // ── 时间门：读真实上次合并时刻（锁文件），不是 UtcNow-MinHours 的假值 ──
        var priorMtime = await _lock.ReadLastConsolidatedAtAsync(ct).ConfigureAwait(false);
        if (priorMtime > 0) {
            var hoursSince = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - priorMtime) / 3_600_000.0;
            if (hoursSince < _config.MinHours) {
                Diag.WriteLine($"[DREAM-AUTO] skip: time-gate {hoursSince:F1}h < {_config.MinHours}h");
                return (AutoDreamTriggerOutcome.TimeGate, [], priorMtime);
            }
        }

        // ── 扫描节流：时间门过了也不每回合都扫目录 ──
        var nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sinceScanMs = nowMs - Interlocked.Read(ref _lastScanAtMs);
        if (_config.SessionScanIntervalMs > 0 && sinceScanMs < _config.SessionScanIntervalMs) {
            Diag.WriteLine($"[DREAM-AUTO] skip: scan-throttle {sinceScanMs / 1000}s < {_config.SessionScanIntervalMs / 1000}s");
            return (AutoDreamTriggerOutcome.ScanThrottle, [], priorMtime);
        }

        Interlocked.Exchange(ref _lastScanAtMs, nowMs);

        // ── 会话门：自上次合并以来被写过的会话，排除当前会话 ──
        var sessionIds = await _sessionScanner.ListSessionsTouchedSinceAsync(priorMtime, ct).ConfigureAwait(false);
        var pending = string.IsNullOrEmpty(currentSessionId)
            ? sessionIds.ToList()
            : sessionIds.Where(id => !string.Equals(id, currentSessionId, StringComparison.OrdinalIgnoreCase)).ToList();

        if (pending.Count < _config.MinSessions) {
            Diag.WriteLine($"[DREAM-AUTO] skip: session-gate {pending.Count} < {_config.MinSessions}");
            return (AutoDreamTriggerOutcome.SessionGate, pending, priorMtime);
        }

        // ── 占锁：原子抢，抢不到说明另一进程/另一回合正在整合 ──
        var acquired = await _lock.TryAcquireAsync(ct, _isProcessAlive).ConfigureAwait(false);
        if (acquired is null) {
            Diag.WriteLine("[DREAM-AUTO] skip: lock-held");
            return (AutoDreamTriggerOutcome.LockHeld, pending, priorMtime);
        }

        return (AutoDreamTriggerOutcome.Fired, pending, acquired.Value);
    }

    private async Task<AutoDreamTriggerOutcome> ExecuteAsync(
        IReadOnlyList<string> sessionIds,
        long priorMtime,
        CancellationToken ct) {
        try {
            var result = await _dreamFeature
                .ExecuteAsync(new DreamRequest(Force: true, SessionIds: sessionIds), ct)
                .ConfigureAwait(false);

            if (result.IsSuccess) {
                await _lock.WriteNowAsync(ct).ConfigureAwait(false);
                Diag.WriteLine($"[DREAM-AUTO] done task={result.TaskId} sessions={result.SessionsProcessed} {result.ExecutionTimeMs}ms");
                return AutoDreamTriggerOutcome.Fired;
            }

            // 无论跳过还是失败，只要已经占过锁就必须回滚 —— 否则锁体留着「刚合并完」的
            // 时刻戳，时间门会把下一个 24 小时全挡掉，而记忆一条都没整合。
            await _lock.RollbackAsync(priorMtime, CancellationToken.None).ConfigureAwait(false);
            if (result.IsSkipped) {
                Diag.WriteLine($"[DREAM-AUTO] skipped-by-feature {result.Content}");
            } else {
                Diag.WriteLine($"[DREAM-AUTO] fail {result.Content}");
            }

            return AutoDreamTriggerOutcome.Failed;
        } catch (Exception ex) {
            // 含 OperationCanceledException：后台动作不能把取消抛给宿主。
            await _lock.RollbackAsync(priorMtime, CancellationToken.None).ConfigureAwait(false);
            _logger?.LogWarning(ex, "[AutoDreamTrigger] 自动整合失败（已回滚锁）");
            Diag.WriteLine($"[DREAM-AUTO] fail {ex.GetType().Name}: {ex.Message}");
            return AutoDreamTriggerOutcome.Failed;
        }
    }
}
