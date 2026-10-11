namespace Infrastructure.Pipeline.Middlewares;


/// <summary>
/// 通用超时中间件（接口约束版）— 从 ITimeoutContext.Timeout 读取超时时长
/// </summary>
public sealed class TimeoutMiddleware<TContext> : IMiddleware<TContext>
    where TContext : ITimeoutContext {

    /// <summary>
    /// 执行中间件 — 从上下文读取超时时长,超时则标记上下文并抛出 TimeoutException
    /// </summary>
    /// <param name="context">中间件上下文,需提供 Timeout 与 IsTimedOut</param>
    /// <param name="next">下游委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct) {
        using var cts = TimeoutHelper.CreateLinkedTimeout(ct, context.Timeout);

        try {
            await next(context, cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            context.IsTimedOut = true;
            throw new TimeoutException($"[INF026] 操作在 {context.Timeout.TotalMilliseconds}ms 内未完成");
        }
    }
}

/// <summary>
/// 通用超时中间件（固定超时版）— 适用于任意 Context，不要求实现 ITimeoutContext
/// </summary>
public sealed class FixedTimeoutMiddleware<TContext>(TimeSpan _timeout) : IMiddleware<TContext> {

    /// <summary>
    /// 执行中间件 — 使用构造时指定的固定超时,超时抛出 TimeoutException
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下游委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct) {
        using var cts = TimeoutHelper.CreateLinkedTimeout(ct, _timeout);

        try {
            await next(context, cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
            throw new TimeoutException($"[INF027] 操作在 {_timeout.TotalMilliseconds}ms 内未完成");
        }
    }
}