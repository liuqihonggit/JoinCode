namespace Core.Bridge.Gate;

/// <summary>
/// 桥接初始化门控中间件 — 检查 OAuth 令牌过期且刷新失败时,阻止后续初始化并标记失败
/// </summary>
public sealed class BridgeGateExpiredTokenMiddleware : IBridgeInitGateMiddleware {
    /// <summary>
    /// 执行中间件 — 若令牌已过期且刷新失败,记录死令牌并终止;否则调用后续中间件
    /// </summary>
    /// <param name="ctx">桥接初始化门控上下文</param>
    /// <param name="next">后续中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>异步任务</returns>
    public async Task InvokeAsync(BridgeInitGateContext ctx, MiddlewareDelegate<BridgeInitGateContext> next, CancellationToken ct) {
        var expiryAfterRefresh = ctx.Options.GetOAuthTokenExpiry?.Invoke();
        var clock = ctx.Clock ?? JoinCode.Abstractions.Clock.SystemClockService.Instance;
        if (expiryAfterRefresh.HasValue && expiryAfterRefresh.Value <= clock.GetUtcNowOffset()) {
            ctx.Logger?.LogDebug("Bridge: skipping - OAuth token expired and refresh failed (re-login required)");
            ctx.Options.OnStateChange?.Invoke(BridgeState.Failed, "/login");

            if (ctx.Options.DeadTokenState is not null) {
                try {
                    await ctx.Options.DeadTokenState.RecordDeadTokenAsync(expiryAfterRefresh.Value).ConfigureAwait(false);
                } catch (Exception ex) {
                    ctx.Logger?.LogDebug(ex, "Bridge: failed to record dead token state");
                }
            }

            ctx.Fail("OAuth token expired");
            return;
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}