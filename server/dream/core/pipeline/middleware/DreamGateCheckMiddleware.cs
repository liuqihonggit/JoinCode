namespace JoinCode.Dream.Pipeline;

/// <summary>
/// 做梦门控检查中间件 — 校验是否满足自动做梦的触发条件
/// </summary>
[Register(typeof(IDreamMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DreamGateCheckMiddleware : ServiceEntity, IDreamMiddleware
{
    private readonly ISessionScanner _sessionScanner;
    private readonly AutoDreamConfig _config;
    private readonly ILogger<DreamGateCheckMiddleware>? _logger;

    /// <summary>
    /// 构造做梦门控检查中间件
    /// </summary>
    /// <param name="sessionScanner">会话扫描器</param>
    /// <param name="config">自动做梦配置</param>
    /// <param name="logger">日志记录器（可选）</param>
    public DreamGateCheckMiddleware(ISessionScanner sessionScanner, AutoDreamConfig config, ILogger<DreamGateCheckMiddleware>? logger = null)
    {
        _sessionScanner = sessionScanner;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 执行门控检查 — 强制模式直接通过；否则校验启用状态和最小会话数
    /// </summary>
    /// <param name="ctx">做梦上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(DreamContext ctx, MiddlewareDelegate<DreamContext> next, CancellationToken ct)
    {
        if (ctx.Request.Force)
        {
            ctx.GateChecked = true;
            await next(ctx, ct).ConfigureAwait(false);
            return;
        }

        if (!_config.Enabled)
        {
            _logger?.LogDebug("[DreamGate] 自动做梦已禁用");
            ctx.Result = DreamResult.Skipped("门控未通过: 自动做梦已禁用");
            return;
        }

        var lastConsolidationTime = DateTime.UtcNow.AddHours(-_config.MinHours).Ticks / TimeSpan.TicksPerMillisecond;
        var sessions = await _sessionScanner.ListSessionsTouchedSinceAsync(lastConsolidationTime, ct).ConfigureAwait(false);

        if (sessions.Count < _config.MinSessions)
        {
            _logger?.LogDebug("[DreamGate] 会话数不足: {Count} < {Min}", sessions.Count, _config.MinSessions);
            ctx.Result = DreamResult.Skipped($"门控未通过: 会话数不足: {sessions.Count} < {_config.MinSessions}");
            return;
        }

        ctx.GateChecked = true;
        await next(ctx, ct).ConfigureAwait(false);
    }
}
