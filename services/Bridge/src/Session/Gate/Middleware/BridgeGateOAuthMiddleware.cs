namespace Core.Bridge.Gate;

public sealed class BridgeGateOAuthMiddleware : IBridgeInitGateMiddleware
{
    public Task InvokeAsync(BridgeInitGateContext ctx, MiddlewareDelegate<BridgeInitGateContext> next, CancellationToken ct)
    {
        var accessToken = ctx.GetAccessToken();
        if (string.IsNullOrEmpty(accessToken))
        {
            ctx.Logger?.LogDebug("Bridge: skipping - no OAuth tokens");
            ctx.Options.OnStateChange?.Invoke(BridgeState.Failed, "/login");
            ctx.Fail("no OAuth tokens");
            return Task.CompletedTask;
        }

        ctx.AccessToken = accessToken;
        return next(ctx, ct);
    }
}
