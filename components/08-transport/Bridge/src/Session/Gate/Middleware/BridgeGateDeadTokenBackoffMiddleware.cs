namespace Core.Bridge.Gate;

public sealed class BridgeGateDeadTokenBackoffMiddleware : IBridgeInitGateMiddleware
{
    public Task InvokeAsync(BridgeInitGateContext ctx, MiddlewareDelegate<BridgeInitGateContext> next, CancellationToken ct)
    {
        if (ctx.Options.DeadTokenState is not null)
        {
            var deadState = ctx.Options.DeadTokenState;
            var currentExpiry = ctx.Options.GetOAuthTokenExpiry?.Invoke();
            if (currentExpiry.HasValue &&
                deadState.DeadExpiresAt.HasValue &&
                deadState.DeadExpiresAt.Value == currentExpiry.Value &&
                deadState.DeadFailCount >= 3)
            {
                ctx.Logger?.LogDebug("Bridge: skipping - cross-process backoff (dead token seen {Count} times)", deadState.DeadFailCount);
                ctx.Fail("cross-process dead token backoff");
                return Task.CompletedTask;
            }
        }

        return next(ctx, ct);
    }
}
