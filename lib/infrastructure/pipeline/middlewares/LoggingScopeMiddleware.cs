namespace Infrastructure.Pipeline.Middlewares;

/// <summary>
/// 日志 Scope 中间件 — 在管道入口自动开 BeginScope，后续所有日志自动携带 TraceId + ObjectId
/// 必须作为管道第一个中间件（最先 Use），确保后续所有中间件的日志都在 scope 内
/// 通过 objectIdSelector 从 context 提取 ObjectId，默认 Entity 直接取、其余返回 Empty
/// </summary>
public sealed class LoggingScopeMiddleware<TContext>(
    ILogger<LoggingScopeMiddleware<TContext>>? logger = null,
    Func<TContext, ObjectId>? objectIdSelector = null) : IMiddleware<TContext>
{
    private readonly Func<TContext, ObjectId> _objectIdSelector =
        objectIdSelector ?? (ctx => ctx is Entity e ? e.ObjectId : ObjectId.Empty);

    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        var activity = Activity.Current;
        var state = new LogScopeState(
            activity?.TraceId.ToString(),
            activity?.SpanId.ToString(),
            _objectIdSelector(context));
        using var scope = logger?.BeginScope(state);
        await next(context, ct).ConfigureAwait(false);
    }
}

/// <summary>
/// 流式日志 Scope 中间件 — Stream 管道的 BeginScope 包装
/// </summary>
public sealed class StreamLoggingScopeMiddleware<TContext, TEvent>(
    ILogger<StreamLoggingScopeMiddleware<TContext, TEvent>>? logger = null,
    Func<TContext, ObjectId>? objectIdSelector = null) : IStreamMiddleware<TContext, TEvent>
{
    private readonly Func<TContext, ObjectId> _objectIdSelector =
        objectIdSelector ?? (ctx => ctx is Entity e ? e.ObjectId : ObjectId.Empty);

    public async IAsyncEnumerable<TEvent> InvokeAsync(
        TContext context,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var activity = Activity.Current;
        var state = new LogScopeState(
            activity?.TraceId.ToString(),
            activity?.SpanId.ToString(),
            _objectIdSelector(context));
        using var scope = logger?.BeginScope(state);
        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}
