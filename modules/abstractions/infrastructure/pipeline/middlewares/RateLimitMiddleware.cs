namespace Infrastructure.Pipeline.Middlewares;

/// <summary>
/// 固定窗口速率限制中间件 — 在指定时间窗口内最多允许 maxRequests 次请求，超出抛出 <see cref="RateLimitExceededException"/>
/// </summary>
public sealed class FixedRateLimitMiddleware<TContext>(
    int maxRequests,
    TimeSpan window) : IMiddleware<TContext> {
    private readonly FixedWindowRateLimiter _limiter = new(maxRequests, window);


    /// <inheritdoc/>
    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct) {
        if (!_limiter.TryAcquire()) {
            throw new RateLimitExceededException($"[INF024] 速率限制: 每{window.TotalSeconds}s 最多{maxRequests}次请求");
        }

        await next(context, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 速率限制超限异常 — 当请求速率超过中间件配置的窗口上限时抛出
/// </summary>
public sealed class RateLimitExceededException(string message) : Exception(message);