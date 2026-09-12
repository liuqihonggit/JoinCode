namespace Infrastructure.Pipeline;

/// <summary>
/// Stream 管道 — 从 DI 注入的中间件集合构建
/// Stream 中间件默认 OnError=Propagate，因为流式场景异常传播更安全
/// </summary>
public sealed class StreamMiddlewarePipeline<TContext, TEvent>
{
    private readonly StreamMiddlewareDelegate<TContext, TEvent> _pipeline;
    private readonly PipelinePreHookDelegate<TContext>? _onPreExecute;
    private readonly PipelinePostHookDelegate<TContext>? _onPostExecute;

    public StreamMiddlewarePipeline(
        IEnumerable<IStreamMiddleware<TContext, TEvent>> middlewares,
        Action<TContext, Exception>? onError = null,
        PipelinePreHookDelegate<TContext>? onPreExecute = null,
        PipelinePostHookDelegate<TContext>? onPostExecute = null,
        Func<TContext, bool>? shortCircuitPredicate = null)
    {
        _onPreExecute = onPreExecute;
        _onPostExecute = onPostExecute;
        _pipeline = Build(middlewares, onError, shortCircuitPredicate);
    }

    /// <summary>
    /// 执行管道 — Pre Hook → 中间件链 → Post Hook
    /// Pre Hook 返回 false 时 yield 空序列
    /// </summary>
    public async IAsyncEnumerable<TEvent> ExecuteAsync(TContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        // Pre Hook: 返回 false 则短路，yield 空序列
        if (_onPreExecute is not null && !await _onPreExecute(context, ct).ConfigureAwait(false))
        {
            yield break;
        }

        try
        {
            await foreach (var evt in _pipeline(context, ct).ConfigureAwait(false))
            {
                yield return evt;
            }
        }
        finally
        {
            // Post Hook: 无论成功或失败都执行
            if (_onPostExecute is not null)
            {
                await _onPostExecute(context, ct).ConfigureAwait(false);
            }
        }
    }

    private static StreamMiddlewareDelegate<TContext, TEvent> Build(
        IEnumerable<IStreamMiddleware<TContext, TEvent>> middlewares,
        Action<TContext, Exception>? onError,
        Func<TContext, bool>? shortCircuitPredicate)
    {
        var ordered = middlewares.ToArray();
        StreamMiddlewareDelegate<TContext, TEvent> pipeline = TerminalHandler;

        for (var i = ordered.Length - 1; i >= 0; i--)
        {
            var current = ordered[i];
            var next = pipeline;

            if (current.OnError == ErrorBehavior.Continue && onError is not null)
            {
                // 自动异常捕获模式
                var handler = onError;
                var pred = shortCircuitPredicate;
                pipeline = (ctx, ct) => pred is not null
                    ? WrapWithShortCircuitAndErrorHandler(current, next, handler, pred, ctx, ct)
                    : WrapWithErrorHandler(current, next, handler, ctx, ct);
            }
            else if (shortCircuitPredicate is not null)
            {
                var pred = shortCircuitPredicate;
                pipeline = (ctx, ct) => WrapWithShortCircuit(current, next, pred, ctx, ct);
            }
            else
            {
                // 传播异常模式
                pipeline = (ctx, ct) => current.InvokeAsync(ctx, next, ct);
            }
        }

        return pipeline;
    }

    /// <summary>
    /// 包裹短路检查的流式中间件调用
    /// </summary>
    private static async IAsyncEnumerable<TEvent> WrapWithShortCircuit(
        IStreamMiddleware<TContext, TEvent> middleware,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        Func<TContext, bool> predicate,
        TContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (predicate(context))
            yield break;

        await foreach (var evt in middleware.InvokeAsync(context, next, ct).ConfigureAwait(false))
        {
            yield return evt;
        }
    }

    /// <summary>
    /// 包裹短路检查 + 异常处理的流式中间件调用
    /// </summary>
    private static async IAsyncEnumerable<TEvent> WrapWithShortCircuitAndErrorHandler(
        IStreamMiddleware<TContext, TEvent> middleware,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        Action<TContext, Exception> handler,
        Func<TContext, bool> predicate,
        TContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (predicate(context))
            yield break;

        var events = new List<TEvent>();
        try
        {
            await foreach (var evt in middleware.InvokeAsync(context, next, ct).ConfigureAwait(false))
            {
                events.Add(evt);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            handler(context, ex);
        }

        foreach (var evt in events)
        {
            yield return evt;
        }
    }

    /// <summary>
    /// 包裹异常处理的流式中间件调用
    /// </summary>
    private static async IAsyncEnumerable<TEvent> WrapWithErrorHandler(
        IStreamMiddleware<TContext, TEvent> middleware,
        StreamMiddlewareDelegate<TContext, TEvent> next,
        Action<TContext, Exception> handler,
        TContext context,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var events = new List<TEvent>();
        try
        {
            await foreach (var evt in middleware.InvokeAsync(context, next, ct).ConfigureAwait(false))
            {
                events.Add(evt);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            handler(context, ex);
        }

        foreach (var evt in events)
        {
            yield return evt;
        }
    }

    private static IAsyncEnumerable<TEvent> TerminalHandler(TContext context, CancellationToken ct)
        => AsyncEnumerable.Empty<TEvent>();
}
