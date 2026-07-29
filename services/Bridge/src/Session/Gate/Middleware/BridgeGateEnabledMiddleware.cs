namespace Core.Bridge.Gate;

public sealed class BridgeGateEnabledMiddleware : IBridgeInitGateMiddleware
{
    public Task InvokeAsync(BridgeInitGateContext ctx, MiddlewareDelegate<BridgeInitGateContext> next, CancellationToken ct)
    {
        if (!ctx.BridgeEnabled)
        {
            ctx.Logger?.LogDebug("Bridge: skipping - bridge not enabled");
            ctx.Fail("bridge not enabled");
            return Task.CompletedTask;
        }

        return next(ctx, ct);
    }
}
