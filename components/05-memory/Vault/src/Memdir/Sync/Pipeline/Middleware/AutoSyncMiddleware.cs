namespace Memdir.Sync;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 自动同步中间件 — 启动定时同步
/// </summary>
[Register(typeof(ISyncStartMiddleware))]
public sealed partial class AutoSyncMiddleware : ISyncStartMiddleware
{

    public Task InvokeAsync(SyncStartContext ctx, MiddlewareDelegate<SyncStartContext> next, CancellationToken ct)
    {
        if (ctx.Options.EnableAutoSync && ctx.SyncTimer is not null)
        {
            ctx.SyncTimer.Change(TimeSpan.Zero, ctx.Options.SyncInterval);
        }

        return next(ctx, ct);
    }
}
