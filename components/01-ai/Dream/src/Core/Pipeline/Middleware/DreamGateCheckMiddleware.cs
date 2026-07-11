namespace JoinCode.Dream.Pipeline;

[Register]
public sealed partial class DreamGateCheckMiddleware : IDreamMiddleware
{
    private readonly ISessionScanner _sessionScanner;
    private readonly AutoDreamConfig _config;
    private readonly ILogger<DreamGateCheckMiddleware>? _logger;

    public DreamGateCheckMiddleware(ISessionScanner sessionScanner, AutoDreamConfig config, ILogger<DreamGateCheckMiddleware>? logger = null)
    {
        _sessionScanner = sessionScanner;
        _config = config;
        _logger = logger;
    }

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
