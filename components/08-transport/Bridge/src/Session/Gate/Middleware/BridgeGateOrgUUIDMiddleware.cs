namespace Core.Bridge.Gate;

public sealed class BridgeGateOrgUUIDMiddleware : IBridgeInitGateMiddleware
{
    public Task InvokeAsync(BridgeInitGateContext ctx, MiddlewareDelegate<BridgeInitGateContext> next, CancellationToken ct)
    {
        var orgUUID = ctx.GetOrgUUID();
        if (string.IsNullOrEmpty(orgUUID))
        {
            ctx.Logger?.LogDebug("Bridge: skipping - no org UUID");
            ctx.Options.OnStateChange?.Invoke(BridgeState.Failed, "/login");
            ctx.Fail("no org UUID");
            return Task.CompletedTask;
        }

        ctx.OrgUUID = orgUUID;
        return next(ctx, ct);
    }
}
