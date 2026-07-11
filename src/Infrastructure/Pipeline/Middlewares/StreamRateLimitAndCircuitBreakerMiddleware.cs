namespace Infrastructure.Pipeline.Middlewares;

public sealed class FixedStreamRateLimitMiddleware<TContext, TEvent>(
    int _maxRequests,
    TimeSpan _window) : IStreamMiddleware<TContext, TEvent>
{
    private readonly object _lock = new();
    private int _currentCount;
    private DateTime _windowStart = DateTime.UtcNow;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async IAsyncEnumerable<TEvent> InvokeAsync(
        TContext context,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!TryAcquire())
        {
            throw new RateLimitExceededException($"速率限制: 每{_window.TotalSeconds}s 最多{_maxRequests}次请求");
        }

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }

    private bool TryAcquire()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if (now - _windowStart >= _window)
            {
                _windowStart = now;
                _currentCount = 0;
            }

            if (_currentCount >= _maxRequests)
            {
                return false;
            }

            _currentCount++;
            return true;
        }
    }
}

public sealed class FixedStreamCircuitBreakerMiddleware<TContext, TEvent>(
    int _failureThreshold,
    TimeSpan _openDuration) : IStreamMiddleware<TContext, TEvent>
{
    private int _consecutiveFailures;
    private DateTime _lastFailureTime = DateTime.MinValue;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async IAsyncEnumerable<TEvent> InvokeAsync(
        TContext context,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
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

        var events = new List<TEvent>();
        try
        {
            await foreach (var evt in next(context, ct).ConfigureAwait(false))
            {
                events.Add(evt);
            }

            _consecutiveFailures = 0;
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            _consecutiveFailures++;
            _lastFailureTime = DateTime.UtcNow;
            throw;
        }

        foreach (var evt in events)
        {
            yield return evt;
        }
    }
}
