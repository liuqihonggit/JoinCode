namespace Core.Bridge;


/// <summary>
/// 关闭注销中间件 — 在桥关闭时注销已注册的环境
/// </summary>
[Register(typeof(IShutdownMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShutdownDeregisterMiddleware : ServiceEntity, IShutdownMiddleware
{

    /// <summary>
    /// 构造关闭注销中间件
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    public ShutdownDeregisterMiddleware(ILogger<ShutdownDeregisterMiddleware>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<ShutdownDeregisterMiddleware>? _logger;

    /// <summary>错误行为策略 — 注销失败时继续执行后续中间件</summary>
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <summary>
    /// 执行注销逻辑 — 非恢复模式下注销环境，然后调用下一中间件
    /// </summary>
    /// <param name="ctx">关闭上下文</param>
    /// <param name="next">下一中间件委托</param>
    /// <param name="ct">取消令牌</param>
    public async Task InvokeAsync(ShutdownContext ctx, MiddlewareDelegate<ShutdownContext> next, CancellationToken ct)
    {
        if (!ctx.IsResuming && ctx.EnvironmentId is not null)
        {
            try
            {
                await (ctx.ApiClient ?? throw new InvalidOperationException("ApiClient not available")).DeregisterEnvironmentAsync(
                    ctx.EnvironmentId, CancellationToken.None).ConfigureAwait(false);
                _logger?.LogInformation("BridgeMain: environment deregistered");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "BridgeMain: deregister failed (non-fatal)");
            }
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
