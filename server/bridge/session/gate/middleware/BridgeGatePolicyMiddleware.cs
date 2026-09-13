namespace Core.Bridge.Gate;

/// <summary>
/// Bridge 门控策略中间件 — 检查组织策略是否允许远程控制
/// </summary>
public sealed class BridgeGatePolicyMiddleware : IBridgeInitGateMiddleware
{
    /// <summary>
    /// 处理门控策略检查 — 若策略不允许 allow_remote_control 则失败终止
    /// </summary>
    /// <param name="ctx">Bridge 初始化门控上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
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
