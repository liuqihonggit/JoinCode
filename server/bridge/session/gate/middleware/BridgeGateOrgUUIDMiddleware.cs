namespace Core.Bridge.Gate;

/// <summary>
/// 桥门控组织 UUID 中间件 — 获取并校验组织 UUID，缺失则失败
/// </summary>
public sealed class BridgeGateOrgUUIDMiddleware : IBridgeInitGateMiddleware
{
    /// <summary>
    /// 执行组织 UUID 获取 — 为空时触发失败状态并终止管道
    /// </summary>
    /// <param name="ctx">桥初始化门控上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
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
