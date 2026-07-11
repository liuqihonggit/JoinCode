namespace Memdir.Sync;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 路径验证中间件 — 验证 WatchPath 非空并确保目录存在
/// </summary>
[Register(typeof(ISyncStartMiddleware))]
public sealed partial class PathValidationMiddleware : ISyncStartMiddleware
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(SyncStartContext ctx, MiddlewareDelegate<SyncStartContext> next, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.Options.WatchPath))
        {
            ctx.Fail($"{nameof(ctx.Options.WatchPath)} is required");
            return Task.CompletedTask;
        }

        if (!ctx.FileSystem.DirectoryExists(ctx.Options.WatchPath))
        {
            ctx.FileSystem.CreateDirectory(ctx.Options.WatchPath);
        }

        return next(ctx, ct);
    }
}
