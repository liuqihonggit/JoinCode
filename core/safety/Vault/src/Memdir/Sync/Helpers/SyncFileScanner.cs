namespace Memdir.Sync.Helpers;

internal sealed class SyncFileScanner
{
    private readonly IFileSystem _fs;
    private readonly IFileOperationService _fileOperationService;
    private readonly TeamMemorySyncOptions _options;
    private readonly ILogger? _logger;
    private readonly ConcurrentDictionary<string, SyncFileEntry> _localEntries;
    private readonly ConcurrentDictionary<string, SyncFileEntry> _remoteEntries;

    private static readonly TeamMemorySyncJsonContext JsonContext = TeamMemorySyncJsonContext.Default;

    internal SyncFileScanner(
        IFileSystem fs,
        IFileOperationService fileOperationService,
        TeamMemorySyncOptions options,
        ILogger? logger,
        ConcurrentDictionary<string, SyncFileEntry> localEntries,
        ConcurrentDictionary<string, SyncFileEntry> remoteEntries)
    {
        _fs = fs;
        _fileOperationService = fileOperationService;
        _options = options;
        _logger = logger;
        _localEntries = localEntries;
        _remoteEntries = remoteEntries;
    }

    internal Task ScanLocalAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.WatchPath) || !_fs.DirectoryExists(_options.WatchPath))
        {
            return Task.CompletedTask;
        }

        foreach (var pattern in _options.FilePatterns)
        {
            var files = _fs.GetFiles(_options.WatchPath, pattern, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var entry = new SyncFileEntry
                {
                    FilePath = file,
                    ContentHash = SyncFileHash.Compute(_fs, file),
                    LastModified = _fs.GetLastWriteTimeUtc(file),
                    Source = "local"
                };

                _localEntries[file] = entry;
            }
        }

        _logger?.LogDebug(L.T(StringKey.VaultLogScanLocalComplete), _localEntries.Count);
        return Task.CompletedTask;
    }

    internal async Task ScanRemoteAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.RemoteStoragePath))
        {
            return;
        }

        try
        {
            var result = await _fileOperationService.ReadFileAsync(
                _options.RemoteStoragePath, cancellationToken: cancellationToken).ConfigureAwait(false);

            if (!result.Success || string.IsNullOrEmpty(result.Content))
            {
                return;
            }

            var entries = RelaxedJsonSerializer.Deserialize(result.Content, JsonContext.ListSyncFileEntry);
            if (entries == null) return;

            foreach (var entry in entries)
            {
                _remoteEntries[entry.FilePath] = entry;
            }

            _logger?.LogDebug(L.T(StringKey.VaultLogScanRemoteComplete), _remoteEntries.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogScanRemoteFailed));
        }
    }
}
