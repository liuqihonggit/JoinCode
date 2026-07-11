namespace Memdir.Sync;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 本地文件扫描中间件 — 扫描 WatchPath 下的文件并填充 LocalEntries
/// </summary>
[Register(typeof(ISyncStartMiddleware))]
public sealed partial class LocalScanMiddleware : ISyncStartMiddleware
{
    [Inject] private readonly ILogger<LocalScanMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    public Task InvokeAsync(SyncStartContext ctx, MiddlewareDelegate<SyncStartContext> next, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.Options.WatchPath) || !ctx.FileSystem.DirectoryExists(ctx.Options.WatchPath))
        {
            return next(ctx, ct);
        }

        foreach (var pattern in ctx.Options.FilePatterns)
        {
            var files = ctx.FileSystem.GetFiles(ctx.Options.WatchPath, pattern, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var entry = new SyncFileEntry
                {
                    FilePath = file,
                    ContentHash = ComputeFileHash(ctx.FileSystem, file),
                    LastModified = ctx.FileSystem.GetLastWriteTimeUtc(file),
                    Source = "local"
                };

                ctx.LocalEntries[file] = entry;
            }
        }

        _logger?.LogDebug(L.T(StringKey.VaultLogScanLocalComplete), ctx.LocalEntries.Count);
        return next(ctx, ct);
    }

    private static string ComputeFileHash(IFileSystem fs, string filePath)
    {
        try
        {
            if (!fs.FileExists(filePath)) return string.Empty;

            var content = fs.ReadAllText(filePath);
            var hash = 0;
            foreach (var c in content)
            {
                hash = ((hash << 5) - hash) + c;
                hash &= 0x7FFFFFFF;
            }

            return hash.ToString("x8");
        }
        catch
        {
            return string.Empty;
        }
    }
}
