namespace Memdir.Sync;


/// <summary>
/// 远程文件扫描中间件 — 从远程存储读取文件索引并填充 RemoteEntries
/// </summary>
[Register(typeof(ISyncStartMiddleware), ServiceLifetime.Singleton)]
public sealed partial class RemoteScanMiddleware : ServiceEntity, ISyncStartMiddleware
{

    /// <summary>
    /// 创建远程文件扫描中间件实例
    /// </summary>
    /// <param name="logger">可选的日志记录器</param>
    public RemoteScanMiddleware(ILogger<RemoteScanMiddleware>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<RemoteScanMiddleware>? _logger;

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public async Task InvokeAsync(SyncStartContext ctx, MiddlewareDelegate<SyncStartContext> next, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(ctx.Options.RemoteStoragePath))
        {
            await next(ctx, ct).ConfigureAwait(false);
            return;
        }

        try
        {
            var result = await ctx.FileOperationService.ReadFileAsync(
                ctx.Options.RemoteStoragePath, cancellationToken: ct).ConfigureAwait(false);

            if (!result.Success || string.IsNullOrEmpty(result.Content))
            {
                await next(ctx, ct).ConfigureAwait(false);
                return;
            }

            var entries = RelaxedJsonSerializer.Deserialize(result.Content, TeamMemorySyncJsonContext.Default.ListSyncFileEntry);
            if (entries is not null)
            {
                foreach (var entry in entries)
                {
                    ctx.RemoteEntries[entry.FilePath] = entry;
                }
            }

            _logger?.LogDebug(L.T(StringKey.VaultLogScanRemoteComplete), ctx.RemoteEntries.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogScanRemoteFailed));
        }

        await next(ctx, ct).ConfigureAwait(false);
    }
}
