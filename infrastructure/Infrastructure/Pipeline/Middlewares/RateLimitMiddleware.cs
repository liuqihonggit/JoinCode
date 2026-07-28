namespace Infrastructure.Pipeline.Middlewares;

public sealed class FixedRateLimitMiddleware<TContext>(
    int maxRequests,
    TimeSpan window) : IMiddleware<TContext>
{
    private readonly FixedWindowRateLimiter _limiter = new(maxRequests, window);


    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        if (!_limiter.TryAcquire())
        {
            throw new RateLimitExceededException($"速率限制: 每{window.TotalSeconds}s 最多{maxRequests}次请求");
        }

        await next(context, ct).ConfigureAwait(false);
    }
}

public sealed class RateLimitExceededException(string message) : Exception(message);
