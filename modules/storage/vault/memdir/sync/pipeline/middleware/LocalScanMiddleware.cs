namespace Memdir.Sync;


/// <summary>
/// 本地文件扫描中间件 — 扫描 WatchPath 下的文件并填充 LocalEntries
/// </summary>
[Register(typeof(ISyncStartMiddleware), ServiceLifetime.Singleton)]
public sealed partial class LocalScanMiddleware : ServiceEntity, ISyncStartMiddleware {

    /// <summary>
    /// 构造本地文件扫描中间件
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    public LocalScanMiddleware(ILogger<LocalScanMiddleware>? logger = null) {
        _logger = logger;
    }
    private readonly ILogger<LocalScanMiddleware>? _logger;


    /// <inheritdoc />
    public async Task InvokeAsync(SyncStartContext ctx, MiddlewareDelegate<SyncStartContext> next, CancellationToken ct) {
        if (string.IsNullOrEmpty(ctx.Options.WatchPath) || !ctx.FileSystem.DirectoryExists(ctx.Options.WatchPath)) {
            await next(ctx, ct).ConfigureAwait(false);
            return;
        }

        foreach (var pattern in ctx.Options.FilePatterns) {
            var files = ctx.FileSystem.GetFiles(ctx.Options.WatchPath, pattern, SearchOption.AllDirectories);
            foreach (var file in files) {
                var entry = new SyncFileEntry {
                    FilePath = file,
                    ContentHash = await ComputeFileHashAsync(ctx.FileSystem, file).ConfigureAwait(false),
                    LastModified = ctx.FileSystem.GetLastWriteTimeUtc(file),
                    Source = "local"
                };

                ctx.LocalEntries[file] = entry;
            }
        }

        _logger?.LogDebug(L.T(StringKey.VaultLogScanLocalComplete), ctx.LocalEntries.Count);
        await next(ctx, ct).ConfigureAwait(false);
    }

    private static async ValueTask<string> ComputeFileHashAsync(IFileSystem fs, string filePath) {
        try {
            if (!fs.FileExists(filePath)) return string.Empty;

            var content = await fs.ReadAllText(filePath).ConfigureAwait(false);
            var hash = 0;
            foreach (var c in content) {
                hash = ((hash << 5) - hash) + c;
                hash &= 0x7FFFFFFF;
            }

            return hash.ToString("x8");
        } catch {
            return string.Empty;
        }
    }
}