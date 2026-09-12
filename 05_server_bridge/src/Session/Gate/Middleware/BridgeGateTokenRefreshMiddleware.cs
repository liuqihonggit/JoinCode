namespace Core.Bridge.Gate;

public sealed class BridgeGateTokenRefreshMiddleware : IBridgeInitGateMiddleware
{
    public async Task InvokeAsync(BridgeInitGateContext ctx, MiddlewareDelegate<BridgeInitGateContext> next, CancellationToken ct)
    {
        if (ctx.Options.CheckAndRefreshOAuthToken is not null)
        {
            try
            {
                var refreshed = await ctx.Options.CheckAndRefreshOAuthToken().ConfigureAwait(false);
                if (!refreshed)
                {
                    ctx.Logger?.LogDebug("Bridge: OAuth token refresh failed");
                }
            }
            catch (Exception ex)
            {
                ctx.Logger?.LogDebug(ex, "Bridge: OAuth token refresh exception");
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
