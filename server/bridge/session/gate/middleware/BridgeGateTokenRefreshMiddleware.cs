namespace Core.Bridge.Gate;

/// <summary>
/// Bridge 初始化门控 OAuth token 刷新中间件 — 进入门控前尝试刷新 OAuth token，失败仅记录日志不中断流程
/// </summary>
public sealed class BridgeGateTokenRefreshMiddleware : IBridgeInitGateMiddleware
{
    /// <summary>
    /// 执行中间件 — 刷新 OAuth token 后调用下一中间件
    /// </summary>
    /// <param name="ctx">Bridge 初始化门控上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步执行操作的任务</returns>
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
