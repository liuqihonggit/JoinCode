namespace JoinCode.Dream.Pipeline;

/// <summary>
/// 做梦门控检查中间件 — 校验是否满足自动做梦的触发条件
/// </summary>
[Register(typeof(IDreamMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DreamGateCheckMiddleware : ServiceEntity, IDreamMiddleware {
    private readonly ISessionScanner _sessionScanner;
    private readonly AutoDreamConfig _config;
    private readonly ILogger<DreamGateCheckMiddleware>? _logger;

    /// <summary>
    /// 构造做梦门控检查中间件
    /// </summary>
    /// <param name="sessionScanner">会话扫描器</param>
    /// <param name="config">自动做梦配置</param>
    /// <param name="logger">日志记录器（可选）</param>
    /// <param name="lock">整合锁（可选 — 缺省 PhysicalFileSystem 实例；测试注入 InMemoryFileSystem 隔离）</param>
    public DreamGateCheckMiddleware(ISessionScanner sessionScanner, AutoDreamConfig config, ILogger<DreamGateCheckMiddleware>? logger = null, ConsolidationLock? @lock = null) {
        _sessionScanner = sessionScanner;
        _config = config;
        _logger = logger;
        _lock = @lock ?? new ConsolidationLock(config, new IO.FileSystem.PhysicalFileSystem());
    }

    /// <summary>整合锁（TASK071 — 时间门真实值 + 成功/回滚落锁；null = 无锁降级旧行为）。</summary>
    private readonly ConsolidationLock? _lock;

    /// <summary>
    /// 执行门控检查 — 强制模式直接通过；否则校验启用状态和最小会话数
    /// </summary>
    /// <param name="ctx">做梦上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(DreamContext ctx, MiddlewareDelegate<DreamContext> next, CancellationToken ct) {
        if (ctx.Request.Force) {
            ctx.GateChecked = true;
            await next(ctx, ct).ConfigureAwait(false);
            return;
        }

        if (!_config.Enabled) {
            _logger?.LogDebug("[DreamGate] 自动做梦已禁用");
            ctx.Result = DreamResult.Skipped("门控未通过: 自动做梦已禁用");
            return;
        }

        // TASK071 服务域-② — 时间门读真实上次合并时刻（锁文件；旧实现 UtcNow-MinHours 假值：
        // 刚合并完仍满足"距上次≥MinHours"→ 重复合并风险）；Force 显式跳过时间门
        var priorMtime = _lock is not null
            ? await _lock.ReadLastConsolidatedAtAsync(ct).ConfigureAwait(false)
            : 0L;
        if (!ctx.Request.Force && priorMtime > 0) {
            var elapsedMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - priorMtime;
            if (elapsedMs < (long)_config.MinHours * 3_600_000) {
                _logger?.LogDebug("[DreamGate] 距上次合并不足 MinHours: {ElapsedMin}min < {Min}h", elapsedMs / 60_000, _config.MinHours);
                ctx.Result = DreamResult.Skipped($"门控未通过: 距上次合并 {elapsedMs / 60_000} 分钟 < 最小间隔 {_config.MinHours} 小时");
                return;
            }
        }

        ctx.PriorMtime = priorMtime;
        ctx.Lock = _lock;

        var sinceMs = priorMtime > 0 ? priorMtime : DateTime.UtcNow.AddHours(-_config.MinHours).Ticks / TimeSpan.TicksPerMillisecond;
        var sessionIds = await _sessionScanner.ListSessionsTouchedSinceAsync(sinceMs, ct).ConfigureAwait(false);

        // TASK071 服务域-③ — 对齐 TS autoDream.ts:163-165：排除当前活跃会话（恒满足 mtime 条件会虚增计数）
        if (!string.IsNullOrEmpty(_config.CurrentSessionId)) {
            sessionIds = sessionIds.Where(id => !string.Equals(id, _config.CurrentSessionId, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        if (sessionIds.Count < _config.MinSessions) {
            _logger?.LogDebug("[DreamGate] 会话数不足: {Count} < {Min}", sessionIds.Count, _config.MinSessions);
            ctx.Result = DreamResult.Skipped($"门控未通过: 会话数不足: {sessionIds.Count} < {_config.MinSessions}");
            return;
        }

        ctx.SessionIds = sessionIds;
        ctx.GateChecked = true;
        await next(ctx, ct).ConfigureAwait(false);
    }
}