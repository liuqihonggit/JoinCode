namespace Core.Bridge;

using JoinCode.Abstractions.Pipeline;

[Register(typeof(IShutdownMiddleware))]
public sealed partial class ShutdownSubprocessesMiddleware : IShutdownMiddleware
{
    [Inject] private readonly ILogger<ShutdownSubprocessesMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(ShutdownContext ctx, MiddlewareDelegate<ShutdownContext> next, CancellationToken ct)
    {
        var handles = ctx.ActiveSessions?.Values.ToList();
        if (handles is not null && handles.Count > 0)
        {
            await ctx.Spawner!.ShutdownAllAsync(handles).ConfigureAwait(false);
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
