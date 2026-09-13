namespace JoinCode.Dream.Pipeline;

/// <summary>
/// Dream 会话扫描中间件 — 扫描需要处理的会话列表，按配置过滤后传递给后续中间件
/// </summary>
[Register(typeof(IDreamMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DreamSessionScanMiddleware : ServiceEntity, IDreamMiddleware
{
    private readonly ISessionScanner _sessionScanner;
    private readonly AutoDreamConfig _config;
    private readonly ILogger<DreamSessionScanMiddleware>? _logger;

    /// <summary>
    /// 构造 DreamSessionScan 中间件
    /// </summary>
    /// <param name="sessionScanner">会话扫描器</param>
    /// <param name="config">Dream 自动配置</param>
    /// <param name="logger">可选日志记录器</param>
    public DreamSessionScanMiddleware(ISessionScanner sessionScanner, AutoDreamConfig config, ILogger<DreamSessionScanMiddleware>? logger = null)
    {
        _sessionScanner = sessionScanner;
        _config = config;
        _logger = logger;
    }

    /// <summary>
    /// 执行会话扫描中间件 — 按请求 SessionIds 或扫描最近会话，不足最小数量时跳过
    /// </summary>
    /// <param name="ctx">Dream 管道上下文</param>
    /// <param name="next">管道下一个委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(DreamContext ctx, MiddlewareDelegate<DreamContext> next, CancellationToken ct)
    {
        if (ctx.Request.SessionIds?.Count > 0)
        {
            ctx.SessionIds = ctx.Request.SessionIds;
        }
        else
        {
            var lastConsolidationTime = DateTime.UtcNow.AddHours(-_config.MinHours).Ticks / TimeSpan.TicksPerMillisecond;
            ctx.SessionIds = await _sessionScanner.ListSessionsTouchedSinceAsync(lastConsolidationTime, ct).ConfigureAwait(false);
        }

        if (!ctx.SessionIds.Any())
        {
            _logger?.LogDebug("[DreamScan] 没有找到需要处理的会话");
            ctx.Result = DreamResult.Skipped("没有需要处理的会话");
            return;
        }

        var sessionCount = ctx.SessionIds.Count();
        if (!ctx.Request.Force && sessionCount < _config.MinSessions)
        {
            _logger?.LogDebug("[DreamScan] 会话数量不足: {Count} < {Min}", sessionCount, _config.MinSessions);
            ctx.Result = DreamResult.Skipped($"会话数量不足: {sessionCount} < {_config.MinSessions}");
            return;
        }

        ctx.SessionsScanned = true;
        await next(ctx, ct).ConfigureAwait(false);
    }
}
