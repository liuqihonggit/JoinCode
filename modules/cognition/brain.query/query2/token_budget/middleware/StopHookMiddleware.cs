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

        // TASK071 query2-③ — 对齐 TS stopHooks.ts:257-263 blockingError 语义：
        // 仅 BlockingError 非空（hook 主动请求阻断）时回灌，ShouldStop=true（默认停止/查询正常完成）不回灌；
        // 旧实现误用 ShouldStop 判断 → 没 hook 时默认 Stop() 也触发回灌 → 多消耗 AI 轮次（E2E script exhausted 回归）
        if (!string.IsNullOrEmpty(stopResult.BlockingError)) {
            context.StopHookBlockingMessages.Add(stopResult.BlockingError);
        }
    }
}