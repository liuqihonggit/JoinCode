namespace Infrastructure.Pipeline.Middlewares;

/// <summary>
/// 固定阈值断路器中间件 — 连续失败达阈值时熔断，超时后半开重试
/// </summary>
/// <typeparam name="TContext">管道上下文类型</typeparam>
/// <param name="failureThreshold">连续失败阈值</param>
/// <param name="openDuration">熔断开启持续时间</param>
public sealed class FixedCircuitBreakerMiddleware<TContext>(
    int failureThreshold,
    TimeSpan openDuration) : IMiddleware<TContext>
{
    private readonly UnifiedCircuitBreaker _cb = new(typeof(TContext).Name, failureThreshold, openDuration);

    /// <inheritdoc/>
    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        if (!_cb.TryProbe())
        {
            throw new CircuitBreakerOpenException(
                $"断路器开启: 连续{_cb.ConsecutiveFailures}次失败，{openDuration.TotalSeconds}s 后重试");
        }

        try
        {
            await next(context, ct).ConfigureAwait(false);
            _cb.RecordSuccess();
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            _cb.RecordFailure();
            throw;
        }
    }
}

