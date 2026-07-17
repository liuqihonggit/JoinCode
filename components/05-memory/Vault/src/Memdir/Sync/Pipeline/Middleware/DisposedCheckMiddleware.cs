namespace Memdir.Sync;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 已释放/已运行检查中间件 — 短路无效启动请求
/// </summary>
[Register(typeof(ISyncStartMiddleware))]
public sealed partial class DisposedCheckMiddleware : ISyncStartMiddleware
{
    [Inject] private readonly ILogger<DisposedCheckMiddleware>? _logger;


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
