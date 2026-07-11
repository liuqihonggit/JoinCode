namespace Memdir.Sync;

using JoinCode.Abstractions.Pipeline;

/// <summary>
/// 远程文件扫描中间件 — 从远程存储读取文件索引并填充 RemoteEntries
/// </summary>
[Register(typeof(ISyncStartMiddleware))]
public sealed partial class RemoteScanMiddleware : ISyncStartMiddleware
{
    [Inject] private readonly ILogger<RemoteScanMiddleware>? _logger;

    public ErrorBehavior OnError => ErrorBehavior.Continue;

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

            var entries = JsonSerializer.Deserialize(result.Content, TeamMemorySyncJsonContext.Default.ListSyncFileEntry);
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
