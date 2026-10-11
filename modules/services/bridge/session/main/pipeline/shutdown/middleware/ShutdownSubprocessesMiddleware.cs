namespace Core.Bridge;


/// <summary>
/// 关闭子进程中间件 — 关闭所有活跃会话的子进程后传递给下一中间件
/// </summary>
[Register(typeof(IShutdownMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShutdownSubprocessesMiddleware : ServiceEntity, IShutdownMiddleware {
    /// <summary>
    /// 构造关闭子进程中间件
    /// </summary>
    /// <param name="logger">日志记录器</param>
    public ShutdownSubprocessesMiddleware(ILogger<ShutdownSubprocessesMiddleware>? logger = null) {
        _logger = logger;
    }
    private readonly ILogger<ShutdownSubprocessesMiddleware>? _logger;

    /// <summary>错误行为 — 出错时继续后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行中间件 — 关闭所有活跃会话子进程后调用下一中间件
    /// </summary>
    /// <param name="ctx">关闭上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步执行操作的任务</returns>
    public async Task InvokeAsync(ShutdownContext ctx, MiddlewareDelegate<ShutdownContext> next, CancellationToken ct) {
        var handles = ctx.Tracker.Sessions.GetAllHandles().ToList();
        if (handles.Count > 0) {
            await (ctx.Spawner ?? throw new InvalidOperationException("Spawner not available")).ShutdownAllAsync(handles).ConfigureAwait(false);
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}