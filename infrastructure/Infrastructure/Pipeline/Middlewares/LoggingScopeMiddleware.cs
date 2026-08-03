namespace Infrastructure.Pipeline.Middlewares;

/// <summary>
/// 日志 Scope 中间件 — 在管道入口自动开 BeginScope，后续所有日志自动携带 TraceId + ObjectId
/// 必须作为管道第一个中间件（最先 Use），确保后续所有中间件的日志都在 scope 内
/// </summary>
public sealed class LoggingScopeMiddleware<TContext>(
    ILogger<LoggingScopeMiddleware<TContext>>? logger = null) : IMiddleware<TContext>
{
    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        var state = CreateScopeState(context);
        using var scope = logger?.BeginScope(state);
        await next(context, ct).ConfigureAwait(false);
    }

    internal static LogScopeState CreateScopeState(TContext context)
    {
        var activity = Activity.Current;
        var traceId = activity?.TraceId.ToString();
        var spanId = activity?.SpanId.ToString();
        ObjectId? objectId = context switch
        {
            Entity e => e.ObjectId,
            IHasObjectId has => has.ContextObjectId,
            _ => null
        };
        return new LogScopeState(traceId, spanId, objectId);
    }
}

/// <summary>
/// 流式日志 Scope 中间件 — Stream 管道的 BeginScope 包装
/// </summary>
public sealed class StreamLoggingScopeMiddleware<TContext, TEvent>(
    ILogger<StreamLoggingScopeMiddleware<TContext, TEvent>>? logger = null) : IStreamMiddleware<TContext, TEvent>
{
    public async IAsyncEnumerable<TEvent> InvokeAsync(
        TContext context,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var state = LoggingScopeMiddleware<TContext>.CreateScopeState(context);
        using var scope = logger?.BeginScope(state);
        await foreach (var evt in next(context, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }
}
