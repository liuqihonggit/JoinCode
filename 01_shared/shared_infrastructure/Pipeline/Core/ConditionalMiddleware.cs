namespace Infrastructure.Pipeline;

/// <summary>
/// 条件中间件 — predicate 返回 true 时执行内部中间件，否则跳过
/// </summary>
internal sealed class ConditionalMiddleware<TContext>(
    Func<TContext, bool> _predicate,
    IMiddleware<TContext> _inner) : IMiddleware<TContext>
{
    public ErrorBehavior OnError => _inner.OnError;

    public Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
        => _predicate(context)
            ? _inner.InvokeAsync(context, next, ct)
            : next(context, ct);
}

/// <summary>
/// 异步条件中间件 — 异步 predicate 返回 true 时执行内部中间件，否则跳过
/// </summary>
internal sealed class AsyncConditionalMiddleware<TContext>(
    Func<TContext, CancellationToken, ValueTask<bool>> _predicate,
    IMiddleware<TContext> _inner) : IMiddleware<TContext>
{
    public ErrorBehavior OnError => _inner.OnError;

    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        if (await _predicate(context, ct).ConfigureAwait(false))
            await _inner.InvokeAsync(context, next, ct).ConfigureAwait(false);
        else
            await next(context, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 流式条件中间件 — predicate 返回 true 时执行内部中间件，否则跳过
/// </summary>
internal sealed class ConditionalStreamMiddleware<TContext, TEvent>(
    Func<TContext, bool> _predicate,
    IStreamMiddleware<TContext, TEvent> _inner) : IStreamMiddleware<TContext, TEvent>
{
    public ErrorBehavior OnError => _inner.OnError;

    public IAsyncEnumerable<TEvent> InvokeAsync(
        TContext context,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        CancellationToken ct)
        => _predicate(context)
            ? _inner.InvokeAsync(context, next, ct)
            : next(context, ct);
}
