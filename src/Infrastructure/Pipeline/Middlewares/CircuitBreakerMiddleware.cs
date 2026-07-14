namespace Infrastructure.Pipeline.Middlewares;

public sealed class FixedCircuitBreakerMiddleware<TContext>(
    int failureThreshold,
    TimeSpan openDuration) : IMiddleware<TContext>
{
    private readonly CircuitBreakerState _state = new(failureThreshold, openDuration);

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        if (_state.ShouldTrip())
        {
            throw new CircuitBreakerOpenException(
                $"断路器开启: 连续{_state.ConsecutiveFailures}次失败，{openDuration.TotalSeconds}s 后重试");
        }

        try
        {
            await next(context, ct).ConfigureAwait(false);
            _state.RecordSuccess();
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            _state.RecordFailure();
            throw;
        }
    }
}

public sealed class CircuitBreakerOpenException(string message) : Exception(message);
