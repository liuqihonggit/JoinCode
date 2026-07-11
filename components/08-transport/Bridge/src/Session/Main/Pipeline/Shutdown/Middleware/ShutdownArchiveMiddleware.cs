namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IShutdownMiddleware))]
public sealed partial class ShutdownArchiveMiddleware : IShutdownMiddleware
{
    [Inject] private readonly ILogger<ShutdownArchiveMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(ShutdownContext ctx, MiddlewareDelegate<ShutdownContext> next, CancellationToken ct)
    {
        if (!ctx.IsResuming && ctx.ArchiveSession is not null)
        {
            var sessionsToArchive = ctx.SessionCompatIds?.ToList();
            if (sessionsToArchive is not null && sessionsToArchive.Count > 0)
            {
                _logger?.LogInformation("BridgeMain: archiving {Count} session(s)", sessionsToArchive.Count);
                foreach (var kvp in sessionsToArchive)
                {
                    try
                    {
                        await ctx.ArchiveSession(kvp.Value, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogDebug(ex, "BridgeMain: archive failed for {SessionId} (non-fatal)", kvp.Value);
                    }
                }
            }
        }
        else if (ctx.IsResuming && !ctx.FatalExit)
        {
            _logger?.LogDebug("BridgeMain: skipping archive+deregister to allow resume");
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
