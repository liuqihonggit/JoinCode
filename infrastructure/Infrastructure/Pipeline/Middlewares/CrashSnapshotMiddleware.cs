namespace Infrastructure.Pipeline.Middlewares;


/// <summary>
/// 通用崩溃快照中间件 — 捕获管道异常自动记录 CrashSnapshot 后重新抛出
/// 注册为管道第一个中间件（最外层），零侵入覆盖所有异常
/// </summary>
public sealed class CrashSnapshotMiddleware<TContext> : IMiddleware<TContext>
{
    private readonly ICrashSnapshotStore _store;
    private readonly string _pipelineName;
    private readonly Func<TContext, CrashExecutionContext?>? _contextExtractor;

    public CrashSnapshotMiddleware(
        ICrashSnapshotStore store,
        string pipelineName,
        Func<TContext, CrashExecutionContext?>? contextExtractor = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipelineName);
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _pipelineName = pipelineName;
        _contextExtractor = contextExtractor;
    }

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public async Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct)
    {
        try
        {
            await next(context, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            var execCtx = _contextExtractor?.Invoke(context)
                ?? new CrashExecutionContext { OperationName = _pipelineName };

            _store.Add(new CrashSnapshot(_pipelineName, CrashSeverity.Error, ex, execCtx));
            throw;
        }
    }
}
