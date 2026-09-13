namespace Infrastructure.Pipeline.Middlewares;


/// <summary>
/// 通用重试中间件（接口约束版）— 降级为透传，网络重试统一由 ResilientHttpExecutor (Gateway) 处理，避免嵌套放大
/// </summary>
public sealed class RetryMiddleware<TContext> : IMiddleware<TContext>
    where TContext : IRetryContext
{
    /// <summary>
    /// 调用下一中间件并清除上下文中的最近错误标记
    /// </summary>
    /// <param name="context">重试上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        await next(context, ct).ConfigureAwait(false);
        context.LastError = null;
    }
}

/// <summary>
/// 通用重试中间件（固定参数版）— 降级为透传，网络重试统一由 ResilientHttpExecutor (Gateway) 处理
/// </summary>
public sealed class FixedRetryMiddleware<TContext>(
    int _maxRetries,
    Func<Exception, bool>? _isRetryable = null) : IMiddleware<TContext>
{
    /// <summary>
    /// 透传调用下一中间件，重试参数已弃用
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        _ = _maxRetries;
        _ = _isRetryable;
        await next(context, ct).ConfigureAwait(false);
    }

    private bool IsRetryable(Exception ex) => _isRetryable?.Invoke(ex) ?? true;
}
