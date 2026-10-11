namespace Core.Bridge;


/// <summary>
/// 关闭归档中间件 — 非恢复模式下归档所有兼容会话 ID；恢复模式下跳过归档以允许后续恢复
/// </summary>
[Register(typeof(IShutdownMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShutdownArchiveMiddleware : ServiceEntity, IShutdownMiddleware {

    /// <summary>
    /// 构造函数 — 注入日志器（可选）
    /// </summary>
    /// <param name="logger">日志器</param>
    public ShutdownArchiveMiddleware(ILogger<ShutdownArchiveMiddleware>? logger = null) {
        _logger = logger;
    }
    private readonly ILogger<ShutdownArchiveMiddleware>? _logger;

    /// <summary>错误行为：继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行归档逻辑 — 非恢复且存在归档委托时归档所有会话；恢复且非致命退出时跳过归档
    /// </summary>
    /// <param name="ctx">关闭上下文</param>
    /// <param name="next">下一管道委托</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task InvokeAsync(ShutdownContext ctx, MiddlewareDelegate<ShutdownContext> next, CancellationToken ct) {
        if (!ctx.IsResuming && ctx.ArchiveSession is not null) {
            var sessionsToArchive = ctx.Tracker.Sessions.GetAllCompatIds().ToList();
            if (sessionsToArchive.Count > 0) {
                _logger?.LogInformation("BridgeMain: archiving {Count} session(s)", sessionsToArchive.Count);
                foreach (var kvp in sessionsToArchive) {
                    try {
                        await ctx.ArchiveSession(kvp.Value, CancellationToken.None).ConfigureAwait(false);
                    } catch (Exception ex) {
                        _logger?.LogDebug(ex, "BridgeMain: archive failed for {SessionId} (non-fatal)", kvp.Value);
                    }
                }
            }
        } else if (ctx.IsResuming && !ctx.FatalExit) {
            _logger?.LogDebug("BridgeMain: skipping archive+deregister to allow resume");
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}