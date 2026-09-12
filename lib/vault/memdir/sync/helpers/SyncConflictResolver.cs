namespace Memdir.Sync.Helpers;

internal sealed class SyncConflictResolver
{
    private readonly SyncFileTransfer _transfer;
    private readonly IClockService _clock;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, SyncFileEntry> _localEntries;
    private readonly ConcurrentDictionary<string, SyncFileEntry> _remoteEntries;
    private readonly SyncEventLog _eventLog;

    internal SyncConflictResolver(
        SyncFileTransfer transfer,
        IClockService clock,
        ILogger? logger,
        ConcurrentDictionary<string, SyncFileEntry> localEntries,
        ConcurrentDictionary<string, SyncFileEntry> remoteEntries,
        SyncEventLog eventLog)
    {
        _transfer = transfer;
        _clock = clock;
        _logger = logger;
        _localEntries = localEntries;
        _remoteEntries = remoteEntries;
        _eventLog = eventLog;
    }

    internal async Task<SyncConflictResolution> ResolveAsync(string filePath, SyncConflictResolution resolution, CancellationToken cancellationToken)
    {
        var localEntry = _localEntries.TryGetValue(filePath, out var l) ? l : null;
        var remoteEntry = _remoteEntries.TryGetValue(filePath, out var r) ? r : null;

        if (localEntry == null || remoteEntry == null)
        {
            _logger?.LogWarning(L.T(StringKey.VaultLogConflictMissingEntry), filePath);
            return resolution;
        }

        var resolved = resolution switch
        {
            SyncConflictResolution.KeepLocal => await ApplyLocalToRemoteAsync(filePath, cancellationToken).ConfigureAwait(false),
            SyncConflictResolution.KeepRemote => await ApplyRemoteToLocalAsync(filePath, cancellationToken).ConfigureAwait(false),
            SyncConflictResolution.KeepNewest => await ApplyNewestAsync(filePath, localEntry, remoteEntry, cancellationToken).ConfigureAwait(false),
            SyncConflictResolution.Merge => await ApplyMergeAsync(filePath, cancellationToken).ConfigureAwait(false),
            _ => false
        };

        _eventLog.Enqueue(new MemorySyncEvent
        {
            EventId = Guid.NewGuid().ToString("N")[..8],
            FilePath = filePath,
            Type = resolved ? SyncEventType.ConflictResolved : SyncEventType.Error,
            Timestamp = _clock.GetUtcNow(),
            ConflictResolution = resolution,
            ErrorMessage = resolved ? null : L.T(StringKey.VaultConflictResolutionFailed)
        });

        return resolution;
    }

    private async Task<bool> ApplyLocalToRemoteAsync(string filePath, CancellationToken cancellationToken)
    {
        await _transfer.PushToRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ApplyRemoteToLocalAsync(string filePath, CancellationToken cancellationToken)
    {
        await _transfer.PullFromRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ApplyNewestAsync(string filePath, SyncFileEntry localEntry, SyncFileEntry remoteEntry, CancellationToken cancellationToken)
    {
        if (localEntry.LastModified >= remoteEntry.LastModified)
        {
            await _transfer.PushToRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await _transfer.PullFromRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> ApplyMergeAsync(string filePath, CancellationToken cancellationToken)
    {
        await _transfer.PushToRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
        return true;
    }
}
