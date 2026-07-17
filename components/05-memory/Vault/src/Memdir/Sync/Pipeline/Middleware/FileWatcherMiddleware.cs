namespace Memdir.Sync;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 文件监控中间件 — 初始化 IFileSystemWatcher
/// </summary>
[Register(typeof(ISyncStartMiddleware))]
public sealed partial class FileWatcherMiddleware : ISyncStartMiddleware
{

    public Task InvokeAsync(SyncStartContext ctx, MiddlewareDelegate<SyncStartContext> next, CancellationToken ct)
    {
        if (!ctx.Options.EnableFileWatching)
        {
            return next(ctx, ct);
        }

        var watcher = ctx.FileSystem.Watch(ctx.Options.WatchPath);
        watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
        watcher.IncludeSubdirectories = true;

        foreach (var pattern in ctx.Options.FilePatterns)
        {
            watcher.Filters.Add(pattern);
        }

        watcher.EnableRaisingEvents = true;
        ctx.Watcher = watcher;

        return next(ctx, ct);
    }
}
