namespace Core.Bridge.Gate;

public sealed class BridgeGatePolicyMiddleware : IBridgeInitGateMiddleware
{
    public Task InvokeAsync(BridgeInitGateContext ctx, MiddlewareDelegate<BridgeInitGateContext> next, CancellationToken ct)
    {
        if (ctx.Options.IsPolicyAllowed is not null && !ctx.Options.IsPolicyAllowed("allow_remote_control"))
        {
            ctx.Logger?.LogDebug("Bridge: skipping - allow_remote_control policy not allowed");
            ctx.Options.OnStateChange?.Invoke(BridgeState.Failed, "disabled by your organization's policy");
            ctx.Fail("policy not allowed");
            return Task.CompletedTask;
        }

        return next(ctx, ct);
    }
}
