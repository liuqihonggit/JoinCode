namespace Core.Bridge;


/// <summary>
/// 关闭管道中间件 — 清理 Bridge 指针文件和刷新定时器
/// 在非恢复模式且单会话模式下清理指针，best-effort 不阻塞主流程
/// </summary>
[Register(typeof(IShutdownMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShutdownClearPointerMiddleware : ServiceEntity, IShutdownMiddleware {
    /// <summary>
    /// 构造 ShutdownClearPointerMiddleware
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    public ShutdownClearPointerMiddleware(ILogger<ShutdownClearPointerMiddleware>? logger = null) {
        _logger = logger;
    }
    private readonly ILogger<ShutdownClearPointerMiddleware>? _logger;

    /// <summary>错误行为 — 继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行关闭清理 — 清理指针文件、释放刷新定时器、调用下一中间件
    /// </summary>
    /// <param name="ctx">关闭上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(ShutdownContext ctx, MiddlewareDelegate<ShutdownContext> next, CancellationToken ct) {
        if (!ctx.IsResuming && ctx.SpawnMode == BridgeSpawnMode.SingleSession) {
            try {
                var pointerDir = ctx.ResumePointerDir ?? ctx.WorkingDirectory;
                if (pointerDir is not null) {
                    await (ctx.PointerService ?? throw new InvalidOperationException("PointerService not available")).ClearAsync(pointerDir).ConfigureAwait(false);
                }
            } catch (Exception ex) {
                _logger?.LogDebug(ex, "BridgeMain: pointer clear failed (non-fatal)");
            }
        }

        ctx.PointerRefreshTimer?.Dispose();

        _logger?.LogInformation("BridgeMain: shutdown complete");

        await next(ctx, ct).ConfigureAwait(false);
    }
}