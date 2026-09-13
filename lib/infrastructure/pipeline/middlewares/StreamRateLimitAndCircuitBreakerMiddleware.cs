namespace Infrastructure.Pipeline.Middlewares;

/// <summary>
/// 固定窗口速率限制流式中间件 — 超出窗口配额时抛出 RateLimitExceededException
/// </summary>
public sealed class FixedStreamRateLimitMiddleware<TContext, TEvent>(
    int maxRequests,
    TimeSpan window) : IStreamMiddleware<TContext, TEvent>
{
    private readonly FixedWindowRateLimiter _limiter = new(maxRequests, window);


    /// <summary>
    /// 申请速率配额后透传下一中间件事件流
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>下游事件流</returns>
    /// <exception cref="RateLimitExceededException">超出速率限制时抛出</exception>
    public async IAsyncEnumerable<TEvent> InvokeAsync(
        TContext context,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (!_limiter.TryAcquire())
        {
            throw new RateLimitExceededException($"[INF025] 速率限制: 每{window.TotalSeconds}s 最多{maxRequests}次请求");
        }

        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}

/// <summary>
/// 固定阈值断路器流式中间件 — 连续失败达阈值时开启断路，开启期间请求直接抛出异常
/// </summary>
public sealed class FixedStreamCircuitBreakerMiddleware<TContext, TEvent>(
    int failureThreshold,
    TimeSpan openDuration) : IStreamMiddleware<TContext, TEvent>
{
    private readonly CircuitBreakerState _state = new(failureThreshold, openDuration);


    /// <summary>
    /// 检查断路器状态后透传下一中间件事件流，依据成败更新断路器计数
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>下游事件流</returns>
    /// <exception cref="CircuitBreakerOpenException">断路器开启时抛出</exception>
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
