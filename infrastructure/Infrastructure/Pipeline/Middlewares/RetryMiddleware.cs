namespace Infrastructure.Pipeline.Middlewares;


/// <summary>
/// 通用重试中间件（接口约束版）— 降级为透传，网络重试统一由 ResilientHttpExecutor (Gateway) 处理，避免嵌套放大
/// </summary>
public sealed class RetryMiddleware<TContext> : IMiddleware<TContext>
    where TContext : IRetryContext
{
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
    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        _ = _maxRetries;
        _ = _isRetryable;
        await next(context, ct).ConfigureAwait(false);
    }

    private bool IsRetryable(Exception ex) => _isRetryable?.Invoke(ex) ?? true;
}
