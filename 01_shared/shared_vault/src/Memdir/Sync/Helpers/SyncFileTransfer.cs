namespace Memdir.Sync.Helpers;

internal sealed class SyncFileTransfer : IAsyncDisposable
{
    private readonly IFileSystem _fs;
    private readonly IFileOperationService _fileOperationService;
    private readonly TeamMemorySyncOptions _options;
    private readonly IClockService _clock;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, SyncFileEntry> _localEntries;
    private readonly ConcurrentDictionary<string, SyncFileEntry> _remoteEntries;
    private readonly SyncEventLog _eventLog;

    private static readonly TeamMemorySyncJsonContext JsonContext = TeamMemorySyncJsonContext.Default;

    internal SyncFileTransfer(
        IFileSystem fs,
        IFileOperationService fileOperationService,
        TeamMemorySyncOptions options,
        IClockService clock,
        ILogger? logger,
        ConcurrentDictionary<string, SyncFileEntry> localEntries,
        ConcurrentDictionary<string, SyncFileEntry> remoteEntries,
        SyncEventLog eventLog)
    {
        _fs = fs;
        _fileOperationService = fileOperationService;
        _options = options;
        _clock = clock;
        _logger = logger;
        _localEntries = localEntries;
        _remoteEntries = remoteEntries;
        _eventLog = eventLog;
    }

    internal async Task PushToRemoteAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.RemoteStoragePath)) return;

        try
        {
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var hash = SyncFileHash.Compute(_fs, filePath);
            var lastModified = _fs.GetLastWriteTimeUtc(filePath);

            var entry = new SyncFileEntry
            {
                FilePath = filePath,
                ContentHash = hash,
                LastModified = lastModified,
                Source = "local"
            };

            _remoteEntries[filePath] = entry;

            await PersistRemoteIndexAsync(cancellationToken).ConfigureAwait(false);

            _eventLog.Enqueue(new MemorySyncEvent
            {
                EventId = Guid.NewGuid().ToString("N")[..8],
                FilePath = filePath,
                Type = SyncEventType.Synced,
                Timestamp = _clock.GetUtcNow(),
                ContentHash = hash
            });

            _logger?.LogDebug(L.T(StringKey.VaultLogPushToRemote), filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogPushToRemoteFailed), filePath);
        }
    }

    internal async Task PullFromRemoteAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.RemoteStoragePath)) return;

        try
        {
            if (!_remoteEntries.TryGetValue(filePath, out var remoteEntry)) return;

            var dir = Path.GetDirectoryName(filePath);
            DirectoryHelper.EnsureDirectoryExists(_fs, dir);

            var content = await _fileOperationService.ReadFileAsync(
                Path.Combine(_options.RemoteStoragePath, Path.GetFileName(filePath)),
                cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!content.Success) return;

            await _fs.WriteAllTextAsync(filePath, content.Content, cancellationToken).ConfigureAwait(false);

            var entry = new SyncFileEntry
            {
                FilePath = filePath,
                ContentHash = remoteEntry.ContentHash,
                LastModified = remoteEntry.LastModified,
                Source = "remote"
            };

            _localEntries[filePath] = entry;

            _eventLog.Enqueue(new MemorySyncEvent
            {
                EventId = Guid.NewGuid().ToString("N")[..8],
                FilePath = filePath,
                Type = SyncEventType.Synced,
                Timestamp = _clock.GetUtcNow(),
                ContentHash = remoteEntry.ContentHash
            });

            _logger?.LogDebug(L.T(StringKey.VaultLogPullFromRemote), filePath);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogPullFromRemoteFailed), filePath);
        }
    }

    internal async Task PersistRemoteIndexAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.RemoteStoragePath)) return;

        try
        {
            var entries = _remoteEntries.Values.ToList();
            var json = RelaxedJsonSerializer.Serialize(entries, JsonContext);

            await _fileOperationService.WriteFileAsync(
                _options.RemoteStoragePath, json, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogPersistRemoteIndexFailed));
        }
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
