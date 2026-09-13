namespace Core.Bridge.Gate;

/// <summary>
/// 桥初始化门控死令牌退避中间件 — 跨进程共享死令牌状态，连续失败 3 次以上时跳过初始化
/// </summary>
public sealed class BridgeGateDeadTokenBackoffMiddleware : IBridgeInitGateMiddleware
{
    /// <summary>
    /// 执行中间件 — 检查死令牌退避状态，满足退避条件时失败终止，否则继续管道
    /// </summary>
    /// <param name="ctx">桥初始化门控上下文</param>
    /// <param name="next">下一个中间件委托</param>
    /// <param name="ct">取消令牌</param>
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
