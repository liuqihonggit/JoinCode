namespace Memdir.Sync;


/// <summary>
/// 已释放/已运行检查中间件 — 短路无效启动请求
/// </summary>
[Register(typeof(ISyncStartMiddleware), ServiceLifetime.Singleton)]
public sealed partial class DisposedCheckMiddleware : ServiceEntity, ISyncStartMiddleware
{

    /// <summary>
    /// 构造已释放/已运行检查中间件
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    public DisposedCheckMiddleware(ILogger<DisposedCheckMiddleware>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<DisposedCheckMiddleware>? _logger;


    /// <inheritdoc/>
    public Task InvokeAsync(SyncStartContext ctx, MiddlewareDelegate<SyncStartContext> next, CancellationToken ct)
    {
        if (ctx.IsDisposed)
        {
            ctx.Fail("Service is disposed");
            return Task.CompletedTask;
        }

        if (ctx.IsAlreadyRunning)
        {
            _logger?.LogDebug(L.T(StringKey.VaultLogSyncAlreadyRunning));
            return Task.CompletedTask;
        }

        return next(ctx, ct);
    }
}
