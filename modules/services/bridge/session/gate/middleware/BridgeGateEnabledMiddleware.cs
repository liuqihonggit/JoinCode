namespace Core.Bridge.Gate;

/// <summary>
/// Bridge 初始化门控中间件 — 检查 Bridge 是否启用，未启用时直接失败短路
/// </summary>
public sealed class BridgeGateEnabledMiddleware : IBridgeInitGateMiddleware {
    /// <summary>
    /// 执行中间件逻辑 — Bridge 未启用时调用 ctx.Fail 短路，否则传递给下一管道
    /// </summary>
    /// <param name="ctx">Bridge 初始化门控上下文</param>
    /// <param name="next">下一管道委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task InvokeAsync(BridgeInitGateContext ctx, MiddlewareDelegate<BridgeInitGateContext> next, CancellationToken ct) {
        if (!ctx.BridgeEnabled) {
            ctx.Logger?.LogDebug("Bridge: skipping - bridge not enabled");
            ctx.Fail("bridge not enabled");
            return Task.CompletedTask;
        }

        return next(ctx, ct);
    }
}