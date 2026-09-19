namespace Core.Query;

/// <summary>
/// 停止 Hook 中间件 — 查询完成后执行停止 Hook
/// </summary>
[Register(typeof(IQueryMiddleware), ServiceLifetime.Singleton)]
public sealed partial class StopHookMiddleware : ServiceEntity, IQueryMiddleware {
    /// <summary>
    /// 构造函数 — 注入停止 Hook 管理器（可选）
    /// </summary>
    /// <param name="stopHookManager">停止 Hook 管理器</param>
    public StopHookMiddleware(IQueryStopHookManager? stopHookManager = null) {
        _stopHookManager = stopHookManager;
    }
    private readonly IQueryStopHookManager? _stopHookManager;


    /// <summary>
    /// 错误处理策略 — 继续执行
    /// </summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 注册查询完成钩子执行停止 Hook
    /// </summary>
    /// <param name="context">中间件上下文</param>
    /// <param name="next">下一委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public Task InvokeAsync(QueryMiddlewareContext context, MiddlewareDelegate<QueryMiddlewareContext> next, CancellationToken ct) {
        if (_stopHookManager is not null) {
            context.OnCompleteHooks.Add(ExecuteStopHooksAsync);
        }

        return next(context, ct);
    }

    private async Task ExecuteStopHooksAsync(QueryMiddlewareContext context, CancellationToken ct) {
        var stopHookManager = _stopHookManager ?? throw new InvalidOperationException("StopHookManager not available.");
        var stopResult = await stopHookManager.ExecuteStopHooksAsync("session-id", "query-complete", ct).ConfigureAwait(false);
        if (!stopResult.ShouldStop) {
            // 停止 Hook 建议继续 — 但查询已完成，此标志用于指示是否应继续下一轮
            context.ShouldStop = false;
        }
    }
}