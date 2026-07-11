namespace Infrastructure.Pipeline.Middlewares;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 通用重试中间件（接口约束版）— 从 IRetryContext 读取重试策略
/// </summary>
public sealed class RetryMiddleware<TContext> : IMiddleware<TContext>
    where TContext : IRetryContext
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await next(context, ct).ConfigureAwait(false);
                context.LastError = null;
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (context.IsRetryable(ex) && context.RetryCount < context.MaxRetries)
            {
                context.RetryCount++;
                context.LastError = ex;
                var delay = CalculateDelay(context.RetryCount);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private static TimeSpan CalculateDelay(int retryCount)
    {
        var ms = Math.Min(BaseDelay.TotalMilliseconds * (1 << retryCount), MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(ms);
    }
}

/// <summary>
/// 通用重试中间件（固定参数版）— 适用于任意 Context，不要求实现 IRetryContext
/// </summary>
public sealed class FixedRetryMiddleware<TContext>(
    int _maxRetries,
    Func<Exception, bool>? _isRetryable = null) : IMiddleware<TContext>
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(30);

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        var retryCount = 0;
        while (true)
        {
            try
            {
                await next(context, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) when (IsRetryable(ex) && retryCount < _maxRetries)
            {
                retryCount++;
                var delay = CalculateDelay(retryCount);
                await Task.Delay(delay, ct).ConfigureAwait(false);
            }
        }
    }

    private bool IsRetryable(Exception ex) => _isRetryable?.Invoke(ex) ?? true;

    private static TimeSpan CalculateDelay(int retryCount)
    {
        var ms = Math.Min(BaseDelay.TotalMilliseconds * (1 << retryCount), MaxDelay.TotalMilliseconds);
        return TimeSpan.FromMilliseconds(ms);
    }
}
