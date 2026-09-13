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

    /// <summary>
    /// 构造崩溃快照中间件
    /// </summary>
    /// <param name="store">崩溃快照存储</param>
    /// <param name="pipelineName">管道名称，用于标识快照来源</param>
    /// <param name="contextExtractor">上下文提取器，从 TContext 提取执行上下文信息</param>
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

    /// <summary>
    /// 错误处理行为 — 传播异常（捕获后重新抛出）
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <summary>
    /// 执行管道下一环节，捕获异常记录崩溃快照后重新抛出
    /// </summary>
    /// <param name="context">管道上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
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
