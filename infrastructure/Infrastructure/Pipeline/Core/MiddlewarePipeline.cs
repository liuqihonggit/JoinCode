namespace Infrastructure.Pipeline;

/// <summary>
/// Task 管道 — 从 DI 注入的中间件集合构建，自动处理异常和 Hook 拦截
/// </summary>
public sealed class MiddlewarePipeline<TContext>
{
    private readonly MiddlewareDelegate<TContext> _pipeline;
    private readonly PipelinePreHookDelegate<TContext>? _onPreExecute;
    private readonly PipelinePostHookDelegate<TContext>? _onPostExecute;

    public MiddlewarePipeline(
        IEnumerable<IMiddleware<TContext>> middlewares,
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
    /// </summary>
    public async Task ExecuteAsync(TContext context, CancellationToken ct)
    {
        // Pre Hook: 返回 false 则短路
        if (_onPreExecute is not null && !await _onPreExecute(context, ct).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await _pipeline(context, ct).ConfigureAwait(false);
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

    private static MiddlewareDelegate<TContext> Build(
        IEnumerable<IMiddleware<TContext>> middlewares,
        Action<TContext, Exception>? onError,
        Func<TContext, bool>? shortCircuitPredicate)
    {
        var ordered = middlewares.ToArray();
        MiddlewareDelegate<TContext> pipeline = TerminalHandler;

        for (var i = ordered.Length - 1; i >= 0; i--)
        {
            var current = ordered[i];
            var next = pipeline;

            if (current.OnError == ErrorBehavior.Continue && onError is not null)
            {
                // 自动异常捕获模式：捕获异常 → 调用 onError → 继续下一个中间件
                var handler = onError;
                pipeline = async (ctx, ct) =>
                {
                    if (shortCircuitPredicate?.Invoke(ctx) == true)
                        return;

                    var nextInvoked = false;
                    MiddlewareDelegate<TContext> wrappedNext = (c, t) =>
                    {
                        nextInvoked = true;
                        return next(c, t);
                    };

                    try
                    {
                        await current.InvokeAsync(ctx, wrappedNext, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        handler(ctx, ex);
                        // 如果中间件未调用 next，自动继续下一个中间件
                        if (!nextInvoked)
                        {
                            await next(ctx, ct).ConfigureAwait(false);
                        }
                    }
                };
            }
            else
            {
                // 传播异常模式：异常直接传播，中断管道
                pipeline = shortCircuitPredicate is not null
                    ? async (ctx, ct) =>
                    {
                        if (shortCircuitPredicate(ctx))
                            return;
                        await current.InvokeAsync(ctx, next, ct).ConfigureAwait(false);
                    }
                    : (ctx, ct) => current.InvokeAsync(ctx, next, ct);
            }
        }

        return pipeline;
    }

    private static Task TerminalHandler(TContext context, CancellationToken ct)
        => Task.CompletedTask;
}
