namespace McpToolRegistry;

/// <summary>
/// 崩溃快照中间件 — Order=0（最外层）— 捕获工具执行管道中的异常，自动记录 CrashSnapshot
/// OnError=Continue：捕获异常后记录快照，不中断管道，异常继续传播给外层
/// 零侵入：所有经过管道的异常自动被记录，无需修改任何组件
/// </summary>
[Register]
public sealed partial class CrashSnapshotMiddleware : ServiceEntity, IToolExecutionMiddleware
{
    private readonly ICrashSnapshotStore _store;

    public CrashSnapshotMiddleware(ICrashSnapshotStore store)
    {
        _store = store;
    }

    public ErrorBehavior OnError => ErrorBehavior.Continue;

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
