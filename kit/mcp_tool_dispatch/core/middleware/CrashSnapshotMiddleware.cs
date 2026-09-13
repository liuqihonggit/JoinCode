namespace McpToolRegistry;

/// <summary>
/// 崩溃快照中间件 — Order=0（最外层）— 捕获工具执行管道中的异常，自动记录 CrashSnapshot
/// OnError=Continue：捕获异常后记录快照，不中断管道，异常继续传播给外层
/// 零侵入：所有经过管道的异常自动被记录，无需修改任何组件
/// </summary>
[Register(typeof(IToolExecutionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class CrashSnapshotMiddleware : ServiceEntity, IToolExecutionMiddleware
{
    private readonly ICrashSnapshotStore _store;

    /// <summary>
    /// 构造函数 — 注入崩溃快照存储
    /// </summary>
    /// <param name="store">崩溃快照存储实例，用于持久化工具执行失败快照</param>
    public CrashSnapshotMiddleware(ICrashSnapshotStore store)
    {
        _store = store;
    }

    /// <summary>
    /// 错误处理行为 — Continue 表示记录快照后不中断管道，异常继续向外层传播
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 调用下一层中间件；若结果为错误则构造 CrashSnapshot 并写入存储，实现零侵入异常记录
    /// </summary>
    /// <param name="context">工具执行上下文</param>
    /// <param name="next">下一层中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct)
    {
        await next(context, ct).ConfigureAwait(false);

        if (context.Result is { IsError: true })
        {
            var errorMsg = context.Result.GetFirstText();
            var snapshot = new CrashSnapshot(
                "ToolPipeline",
                CrashSeverity.Error,
                new InvalidOperationException(errorMsg ?? "工具执行失败"),
                new CrashExecutionContext
                {
                    ToolName = context.ToolName,
                    OperationName = "ToolPipeline",
                });

            _store.Add(snapshot);
        }
    }
}
