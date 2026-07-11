namespace Infrastructure.Pipeline.Middlewares;

public sealed class FixedCircuitBreakerMiddleware<TContext>(
    int _failureThreshold,
    TimeSpan _openDuration) : IMiddleware<TContext>
{
    private int _consecutiveFailures;
    private DateTime _lastFailureTime = DateTime.MinValue;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        if (_consecutiveFailures >= _failureThreshold)
        {
            var timeSinceLastFailure = DateTime.UtcNow - _lastFailureTime;
            if (timeSinceLastFailure < _openDuration)
            {
                throw new CircuitBreakerOpenException(
                    $"断路器开启: 连续{_consecutiveFailures}次失败，{_openDuration.TotalSeconds}s 后重试");
            }

            _consecutiveFailures = 0;
        }

        try
        {
            await next(context, ct).ConfigureAwait(false);
            _consecutiveFailures = 0;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            _consecutiveFailures++;
            _lastFailureTime = DateTime.UtcNow;
            throw;
        }
    }
}

public sealed class CircuitBreakerOpenException(string message) : Exception(message);
