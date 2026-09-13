namespace Core.Bridge.Gate;

/// <summary>
/// 桥门控 OAuth 中间件 — 校验访问令牌存在性
/// </summary>
public sealed class BridgeGateOAuthMiddleware : IBridgeInitGateMiddleware
{
    /// <summary>
    /// 执行中间件 — 检查 OAuth 令牌，缺失则失败
    /// </summary>
    /// <param name="ctx">桥初始化门控上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
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
