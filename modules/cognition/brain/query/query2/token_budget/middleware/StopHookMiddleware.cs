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
        // TASK071 query2-③ — sessionId 从查询选项取（旧硬编码 "session-id" 臺会话级钩子永不匹配）
        var sessionId = context.Options?.SessionId ?? "query2-default";
        var stopResult = await stopHookManager.ExecuteStopHooksAsync(sessionId, "query-complete", ct).ConfigureAwait(false);

        // TASK071 query2-③ — 对齐 TS stopHooks.ts:257-263 语义反转修正：
        // ShouldStop=true = hook 请求阻断（blockingError）→ 阻断消息回灌（核心循环继续一轮收尾）；
        // 旧实现在查询完成后设 ShouldStop=false（无效 — 循环已结束），阻断请求被静默丢弃
        if (stopResult.ShouldStop) {
            context.StopHookBlockingMessages.Add(
                stopResult.Message ?? "Stop hook 阻断 — 请继续完成当前任务收尾后再结束");
        }
    }
}