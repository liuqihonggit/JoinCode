namespace Infrastructure.Pipeline.Middlewares;

public sealed class FixedRateLimitMiddleware<TContext>(
    int _maxRequests,
    TimeSpan _window) : IMiddleware<TContext>
{
    private readonly object _lock = new();
    private int _currentCount;
    private DateTime _windowStart = DateTime.UtcNow;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        if (!TryAcquire())
        {
            throw new RateLimitExceededException($"速率限制: 每{_window.TotalSeconds}s 最多{_maxRequests}次请求");
        }

        await next(context, ct).ConfigureAwait(false);
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

public sealed class RateLimitExceededException(string message) : Exception(message);
