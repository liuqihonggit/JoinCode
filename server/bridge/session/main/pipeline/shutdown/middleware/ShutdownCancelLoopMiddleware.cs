namespace Core.Bridge;


/// <summary>
/// 关闭管道中间件 — 取消 Bridge 主循环并注销键盘监听
/// 取消 LoopCts → 等待 LoopTask 退出 → 调用下一中间件
/// </summary>
[Register(typeof(IShutdownMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShutdownCancelLoopMiddleware : ServiceEntity, IShutdownMiddleware {
    /// <summary>
    /// 构造 ShutdownCancelLoopMiddleware
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    public ShutdownCancelLoopMiddleware(ILogger<ShutdownCancelLoopMiddleware>? logger = null) {
        _logger = logger;
    }
    private readonly ILogger<ShutdownCancelLoopMiddleware>? _logger;


    /// <summary>
    /// 执行关闭 — 注销键盘监听、取消主循环、等待循环退出、调用下一中间件
    /// </summary>
    /// <param name="ctx">关闭上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(ShutdownContext ctx, MiddlewareDelegate<ShutdownContext> next, CancellationToken ct) {
        _logger?.LogInformation("BridgeMain: shutting down...");

        ctx.UnregisterKeyboardListener?.Invoke();

        await (ctx.LoopCts?.CancelAsync() ?? Task.CompletedTask).ConfigureAwait(false);

        if (ctx.LoopTask is not null) {
            try {
                await ctx.LoopTask.ConfigureAwait(false);
            } catch (OperationCanceledException) {
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}