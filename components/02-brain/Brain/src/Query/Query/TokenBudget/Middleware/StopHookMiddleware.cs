using JoinCode.Abstractions.Attributes;

namespace Core.Query;

/// <summary>
/// 停止 Hook 中间件 — 查询完成后执行停止 Hook
/// </summary>
[Register(typeof(IQueryMiddleware))]
public sealed partial class StopHookMiddleware : IQueryMiddleware
{
    [Inject] private readonly IQueryStopHookManager? _stopHookManager;


    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册查询完成钩子执行停止 Hook
    /// </summary>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct)
    {
        if (_stopHookManager is not null)
        {
            context.OnCompleteHooks.Add(ExecuteStopHooksAsync);
        }

        return next(context, ct);
    }

    private async Task ExecuteStopHooksAsync(QueryMiddlewareContext context, CancellationToken ct)
    {
        var stopHookManager = _stopHookManager ?? throw new InvalidOperationException("StopHookManager not available.");
        var stopResult = await stopHookManager.ExecuteStopHooksAsync("session-id", "query-complete", ct).ConfigureAwait(false);
        if (!stopResult.ShouldStop)
        {
            // 停止 Hook 建议继续 — 但查询已完成，此标志用于指示是否应继续下一轮
            context.ShouldStop = false;
        }
    }
}
