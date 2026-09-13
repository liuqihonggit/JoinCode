namespace Core.Bridge.Gate;

public sealed class BridgeGateExpiredTokenMiddleware : IBridgeInitGateMiddleware
{
    public async Task InvokeAsync(BridgeInitGateContext ctx, MiddlewareDelegate<BridgeInitGateContext> next, CancellationToken ct)
    {
        var expiryAfterRefresh = ctx.Options.GetOAuthTokenExpiry?.Invoke();
        var clock = ctx.Clock ?? JoinCode.Abstractions.Clock.SystemClockService.Instance;
        if (expiryAfterRefresh.HasValue && expiryAfterRefresh.Value <= clock.GetUtcNowOffset())
        {
            ctx.Logger?.LogDebug("Bridge: skipping - OAuth token expired and refresh failed (re-login required)");
            ctx.Options.OnStateChange?.Invoke(BridgeState.Failed, "/login");

            if (ctx.Options.DeadTokenState is not null)
            {
                try
                {
                    await ctx.Options.DeadTokenState.RecordDeadTokenAsync(expiryAfterRefresh.Value).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    ctx.Logger?.LogDebug(ex, "Bridge: failed to record dead token state");
                }
            }

            ctx.Fail("OAuth token expired");
            return;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
