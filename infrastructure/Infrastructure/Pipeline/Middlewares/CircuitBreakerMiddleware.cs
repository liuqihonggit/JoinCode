namespace Infrastructure.Pipeline.Middlewares;

public sealed class FixedCircuitBreakerMiddleware<TContext>(
    int failureThreshold,
    TimeSpan openDuration) : IMiddleware<TContext>
{
    private readonly UnifiedCircuitBreaker _cb = new(typeof(TContext).Name, failureThreshold, openDuration);

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

