namespace Core.Bridge;


[Register(typeof(IShutdownMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShutdownSubprocessesMiddleware : ServiceEntity, IShutdownMiddleware
{

    public ShutdownSubprocessesMiddleware(ILogger<ShutdownSubprocessesMiddleware>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<ShutdownSubprocessesMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(ShutdownContext ctx, MiddlewareDelegate<ShutdownContext> next, CancellationToken ct)
    {
        var handles = ctx.ActiveSessions.Values.ToList();
        if (handles.Count > 0)
        {
            await (ctx.Spawner ?? throw new InvalidOperationException("Spawner not available")).ShutdownAllAsync(handles).ConfigureAwait(false);
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
