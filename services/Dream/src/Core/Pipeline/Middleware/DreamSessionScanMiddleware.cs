namespace JoinCode.Dream.Pipeline;

[Register]
public sealed partial class DreamSessionScanMiddleware : IDreamMiddleware
{
    private readonly ISessionScanner _sessionScanner;
    private readonly AutoDreamConfig _config;
    private readonly ILogger<DreamSessionScanMiddleware>? _logger;

    public DreamSessionScanMiddleware(ISessionScanner sessionScanner, AutoDreamConfig config, ILogger<DreamSessionScanMiddleware>? logger = null)
    {
        _sessionScanner = sessionScanner;
        _config = config;
        _logger = logger;
    }

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

        if (ctx.SessionIds.Count == 0)
        {
            _logger?.LogDebug("[DreamScan] 没有找到需要处理的会话");
            ctx.Result = DreamResult.Skipped("没有需要处理的会话");
            return;
        }

        if (!ctx.Request.Force && ctx.SessionIds.Count < _config.MinSessions)
        {
            _logger?.LogDebug("[DreamScan] 会话数量不足: {Count} < {Min}", ctx.SessionIds.Count, _config.MinSessions);
            ctx.Result = DreamResult.Skipped($"会话数量不足: {ctx.SessionIds.Count} < {_config.MinSessions}");
            return;
        }

        ctx.SessionsScanned = true;
        await next(ctx, ct).ConfigureAwait(false);
    }
}
