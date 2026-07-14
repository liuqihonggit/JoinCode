namespace Infrastructure.Pipeline.Middlewares;

public sealed class FixedStreamRateLimitMiddleware<TContext, TEvent>(
    int maxRequests,
    TimeSpan window) : IStreamMiddleware<TContext, TEvent>
{
    private readonly FixedWindowRateLimiter _limiter = new(maxRequests, window);

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async IAsyncEnumerable<TEvent> InvokeAsync(
        TContext context,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_limiter.TryAcquire())
        {
            throw new RateLimitExceededException($"速率限制: 每{window.TotalSeconds}s 最多{maxRequests}次请求");
        }

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}

public sealed class FixedStreamCircuitBreakerMiddleware<TContext, TEvent>(
    int failureThreshold,
    TimeSpan openDuration) : IStreamMiddleware<TContext, TEvent>
{
    private readonly CircuitBreakerState _state = new(failureThreshold, openDuration);

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async IAsyncEnumerable<TEvent> InvokeAsync(
        TContext context,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_state.ShouldTrip())
        {
            throw new CircuitBreakerOpenException(
                $"断路器开启: 连续{_state.ConsecutiveFailures}次失败，{openDuration.TotalSeconds}s 后重试");
        }

        var events = new List<TEvent>();
        try
        {
            await foreach (var evt in next(context, ct).ConfigureAwait(false))
            {
                events.Add(evt);
            }

            _state.RecordSuccess();
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            _state.RecordFailure();
            throw;
        }

        foreach (var evt in events)
        {
            yield return evt;
        }
    }
}
