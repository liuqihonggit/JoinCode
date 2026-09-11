namespace Memdir.Sync;

public interface ITeamMemorySyncCommand;

public sealed record StartSyncCmd(TaskCompletionSource Tcs) : ITeamMemorySyncCommand;
public sealed record StopSyncCmd(TaskCompletionSource Tcs) : ITeamMemorySyncCommand;
public sealed record SyncCmd(string? FilePath, TaskCompletionSource Tcs) : ITeamMemorySyncCommand;
public sealed record ResolveConflictCmd(string FilePath, SyncConflictResolution Resolution, TaskCompletionSource<SyncConflictResolution> Tcs) : ITeamMemorySyncCommand;
public sealed record FileChangedCmd(string FilePath) : ITeamMemorySyncCommand;
public sealed record FileDeletedCmd(string FilePath) : ITeamMemorySyncCommand;
public sealed record FileRenamedCmd(string OldPath, string NewPath) : ITeamMemorySyncCommand;

public sealed partial class SyncFileEntry
{
    public required string FilePath { get; init; }
    public required string ContentHash { get; init; }
    public required DateTime LastModified { get; init; }
    public required string Source { get; init; }
}

[Register(typeof(ITeamMemorySyncService), ServiceLifetime.Singleton)]
public sealed partial class TeamMemorySyncService : ActorBase<ITeamMemorySyncCommand, Unit>, ITeamMemorySyncService
{
    private readonly ILogger<TeamMemorySyncService>? _logger;
    private readonly IClockService _clock;
    private readonly ITelemetryService? _telemetryService;
    private readonly TeamMemorySyncOptions _options;
    private readonly IFileOperationService _fileOperationService;
    private readonly IFileSystem _fs;
    private readonly ConcurrentQueue<MemorySyncEvent> _syncHistory = new();
    private readonly ConcurrentDictionary<string, SyncFileEntry> _localEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SyncFileEntry> _remoteEntries = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Threading.Timer _syncTimer;
    private readonly MiddlewarePipeline<SyncStartContext>? _startPipeline;
    private IFileSystemWatcher? _watcher;
    private volatile bool _isRunning;
    private int _disposed;
    private readonly CancellationTokenSource _disposeCts = new();

    private static readonly TeamMemorySyncJsonContext JsonContext = TeamMemorySyncJsonContext.Default;
    private const int MaxSyncHistory = 1000;

    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource<T> CreateTcs<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public TeamMemorySyncService(
        IFileSystem fs,
        IFileOperationService fileOperationService,
        IOptions<TeamMemorySyncOptions>? options = null,
        ILogger<TeamMemorySyncService>? logger = null,
        ILoggerFactory? loggerFactory = null,
        ITelemetryService? telemetryService = null,
        IEnumerable<ISyncStartMiddleware>? startMiddlewares = null,
        IClockService? clock = null)
        : base()
    {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _logger = logger;
        _telemetryService = telemetryService;
        _clock = clock ?? SystemClockService.Instance;
        _options = options?.Value ?? new TeamMemorySyncOptions();

        _syncTimer = new System.Threading.Timer(OnSyncTimerTick, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        if (startMiddlewares is not null && loggerFactory is not null)
        {
            _startPipeline = new PipelineBuilder<SyncStartContext>()
                .WithLoggingScope(loggerFactory)
                .UseRange(startMiddlewares)
                .Build();
        }
        else if (startMiddlewares is not null)
        {
            _startPipeline = new MiddlewarePipeline<SyncStartContext>(startMiddlewares);
        }
    }

    public bool IsRunning => _isRunning;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new StartSyncCmd(tcs), cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new StopSyncCmd(tcs), cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task SyncAsync(string? filePath = null, CancellationToken cancellationToken = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new SyncCmd(filePath, tcs), cancellationToken).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public Task<IEnumerable<MemorySyncEvent>> GetSyncHistoryAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        var events = _syncHistory
            .OrderByDescending(e => e.Timestamp)
            .Take(limit);

        return Task.FromResult<IEnumerable<MemorySyncEvent>>(events);
    }

    public async Task<SyncConflictResolution> ResolveConflictAsync(string filePath, SyncConflictResolution resolution, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(filePath);
        var tcs = CreateTcs<SyncConflictResolution>();
        await SendAsync(new ResolveConflictCmd(filePath, resolution, tcs), cancellationToken).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    private void OnSyncTimerTick(object? state)
    {
        if (_isRunning && Volatile.Read(ref _disposed) == 0)
        {
            TrySend(new SyncCmd(null, null!));
        }
    }

    private void OnFileChanged(object? sender, FileChangedEventArgs e)
    {
        TrySend(new FileChangedCmd(e.FullPath));
    }

    private void OnFileDeleted(object? sender, FileChangedEventArgs e)
    {
        TrySend(new FileDeletedCmd(e.FullPath));
    }

    private void OnFileRenamed(object? sender, FileRenamedEventArgs e)
    {
        TrySend(new FileRenamedCmd(e.OldFullPath, e.FullPath));
    }

    private void InitializeWatcher()
    {
        _watcher = _fs.Watch(_options.WatchPath);
        _watcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
        _watcher.IncludeSubdirectories = true;

        foreach (var pattern in _options.FilePatterns)
        {
            _watcher.Filters.Add(pattern);
        }

        _watcher.Changed += OnFileChanged;
        _watcher.Created += OnFileChanged;
        _watcher.Deleted += OnFileDeleted;
        _watcher.Renamed += OnFileRenamed;

        _watcher.EnableRaisingEvents = true;
    }

    private async Task StartDirectAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (_isRunning)
        {
            _logger?.LogDebug(L.T(StringKey.VaultLogSyncAlreadyRunning));
            return;
        }

        ArgumentException.ThrowIfNullOrEmpty(_options.WatchPath);

        if (!_fs.DirectoryExists(_options.WatchPath))
        {
            _fs.CreateDirectory(_options.WatchPath);
        }

        await ScanLocalFilesAsync(cancellationToken).ConfigureAwait(false);

        if (!string.IsNullOrEmpty(_options.RemoteStoragePath))
        {
            await ScanRemoteFilesAsync(cancellationToken).ConfigureAwait(false);
        }

        if (_options.EnableFileWatching)
        {
            InitializeWatcher();
        }

        if (_options.EnableAutoSync)
        {
            _syncTimer.Change(TimeSpan.Zero, _options.SyncInterval);
        }

        _isRunning = true;
        _logger?.LogInformation(L.T(StringKey.VaultLogSyncStarted), _options.WatchPath);
        RecordSyncMetrics("start", true);
    }

    private async Task StartViaPipelineAsync(CancellationToken cancellationToken)
    {
        var pipeline = _startPipeline;
        if (pipeline is null)
        {
            return;
        }

        var ctx = new SyncStartContext
        {
            FileSystem = _fs,
            FileOperationService = _fileOperationService,
            Options = _options,
            CancellationToken = cancellationToken,
            IsDisposed = Volatile.Read(ref _disposed) != 0,
            IsAlreadyRunning = _isRunning,
            LocalEntries = _localEntries,
            RemoteEntries = _remoteEntries,
            SyncHistory = _syncHistory,
            SyncTimer = _syncTimer,
        };

        await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);

        if (ctx.Failed)
        {
            if (ctx.IsDisposed)
            {
                ObjectDisposedException.ThrowIf(true, this);
            }

            throw new InvalidOperationException(ctx.ErrorMessage ?? "Sync start failed");
        }

        if (ctx.MarkAsRunning)
        {
            _isRunning = true;
        }

        if (ctx.Watcher is not null)
        {
            _watcher = ctx.Watcher;
            _watcher.Changed += OnFileChanged;
            _watcher.Created += OnFileChanged;
            _watcher.Deleted += OnFileDeleted;
            _watcher.Renamed += OnFileRenamed;
        }
    }

    private void StopDirect()
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        if (!_isRunning)
        {
            return;
        }

        _syncTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _watcher?.Dispose();
        _watcher = null;
        _isRunning = false;

        _logger?.LogInformation(L.T(StringKey.VaultLogSyncStopped));
    }

    private async Task SyncSingleFileAsync(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            var localEntry = _localEntries.TryGetValue(filePath, out var l) ? l : null;
            var remoteEntry = _remoteEntries.TryGetValue(filePath, out var r) ? r : null;

            if (localEntry == null && remoteEntry == null)
            {
                return;
            }

            if (localEntry != null && remoteEntry == null)
            {
                await PushToRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (localEntry == null && remoteEntry != null)
            {
                await PullFromRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (localEntry is not null && remoteEntry is not null && localEntry.ContentHash == remoteEntry.ContentHash)
            {
                return;
            }

            var local = localEntry ?? throw new InvalidOperationException($"Local entry is null for {filePath}.");
            var remote = remoteEntry ?? throw new InvalidOperationException($"Remote entry is null for {filePath}.");
            var timeDiff = Math.Abs((local.LastModified - remote.LastModified).TotalSeconds);
            if (timeDiff < _options.ConflictDetectionWindow.TotalSeconds)
            {
                var syncEvent = new MemorySyncEvent
                {
                    EventId = Guid.NewGuid().ToString("N")[..8],
                    FilePath = filePath,
                    Type = SyncEventType.ConflictDetected,
                    Timestamp = _clock.GetUtcNow()
                };

                EnqueueEvent(syncEvent);
                await ResolveConflictDirectAsync(filePath, _options.DefaultConflictResolution, cancellationToken).ConfigureAwait(false);
            }
            else if (localEntry.LastModified > remoteEntry.LastModified)
            {
                await PushToRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await PullFromRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.VaultLogSyncFileFailed), filePath);
            RecordSyncMetrics("sync_file", false);

            EnqueueEvent(new MemorySyncEvent
            {
                EventId = Guid.NewGuid().ToString("N")[..8],
                FilePath = filePath,
                Type = SyncEventType.Error,
                Timestamp = _clock.GetUtcNow(),
                ErrorMessage = ex.Message
            });
        }
    }

    private async Task SyncAllFilesAsync(CancellationToken cancellationToken)
    {
        var allPaths = _localEntries.Keys
            .Union(_remoteEntries.Keys)
            .Distinct()
            .ToList();

        await Task.WhenAll(allPaths.Select(path => SyncSingleFileAsync(path, cancellationToken))).ConfigureAwait(false);
    }

    private async Task<SyncConflictResolution> ResolveConflictDirectAsync(string filePath, SyncConflictResolution resolution, CancellationToken cancellationToken)
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

        var syncEvent = new MemorySyncEvent
        {
            EventId = Guid.NewGuid().ToString("N")[..8],
            FilePath = filePath,
            Type = resolved ? SyncEventType.ConflictResolved : SyncEventType.Error,
            Timestamp = _clock.GetUtcNow(),
            ConflictResolution = resolution,
            ErrorMessage = resolved ? null : L.T(StringKey.VaultConflictResolutionFailed)
        };

        EnqueueEvent(syncEvent);

        return resolution;
    }

    private async Task ScanLocalFilesAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.WatchPath) || !_fs.DirectoryExists(_options.WatchPath))
        {
            return;
        }

        foreach (var pattern in _options.FilePatterns)
        {
            var files = _fs.GetFiles(_options.WatchPath, pattern, SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var entry = new SyncFileEntry
                {
                    FilePath = file,
                    ContentHash = ComputeFileHash(_fs, file),
                    LastModified = _fs.GetLastWriteTimeUtc(file),
                    Source = "local"
                };

                _localEntries[file] = entry;
            }
        }

        _logger?.LogDebug(L.T(StringKey.VaultLogScanLocalComplete), _localEntries.Count);
    }

    private async Task ScanRemoteFilesAsync(CancellationToken cancellationToken)
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

    private async Task PushToRemoteAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(_options.RemoteStoragePath)) return;

        try
        {
            var content = await _fs.ReadAllTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            var hash = ComputeFileHash(_fs, filePath);
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

            EnqueueEvent(new MemorySyncEvent
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

    private async Task PullFromRemoteAsync(string filePath, CancellationToken cancellationToken)
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

            EnqueueEvent(new MemorySyncEvent
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

    private async Task<bool> ApplyLocalToRemoteAsync(string filePath, CancellationToken cancellationToken)
    {
        await PushToRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ApplyRemoteToLocalAsync(string filePath, CancellationToken cancellationToken)
    {
        await PullFromRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ApplyNewestAsync(string filePath, SyncFileEntry localEntry, SyncFileEntry remoteEntry, CancellationToken cancellationToken)
    {
        if (localEntry.LastModified >= remoteEntry.LastModified)
        {
            await PushToRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await PullFromRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
        }

        return true;
    }

    private async Task<bool> ApplyMergeAsync(string filePath, CancellationToken cancellationToken)
    {
        await PushToRemoteAsync(filePath, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task PersistRemoteIndexAsync(CancellationToken cancellationToken)
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

    private void EnqueueEvent(MemorySyncEvent syncEvent)
    {
        _syncHistory.Enqueue(syncEvent);
        while (_syncHistory.Count > MaxSyncHistory)
        {
            _syncHistory.TryDequeue(out _);
        }
    }

    private void RecordSyncMetrics(string operation, bool isSuccess)
        => _telemetryService?.RecordCount("sync.memory.count", new Dictionary<string, string> { ["operation"] = operation, ["success"] = isSuccess.ToString() }, "count", "Memory sync count");

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

    /// <inheritdoc />
    public async Task<TeamSyncStatus> SyncTeamMemoryAsync(string teamId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamId);

        var teamPath = Path.Combine(_options.WatchPath, teamId);
        await SyncAsync(teamPath, cancellationToken).ConfigureAwait(false);

        return GetSyncStatus(teamId) ?? new TeamSyncStatus
        {
            TeamId = teamId,
            IsWatching = IsRunning,
            SyncedMemoryCount = 0
        };
    }

    /// <inheritdoc />
    public TeamSyncStatus? GetSyncStatus(string teamId)
    {
        ArgumentException.ThrowIfNullOrEmpty(teamId);

        var teamPath = Path.Combine(_options.WatchPath, teamId);
        var localCount = _localEntries.Count(e => e.Key.StartsWith(teamPath, StringComparison.OrdinalIgnoreCase));
        var conflictEvents = _syncHistory
            .Where(e => e.FilePath.StartsWith(teamPath, StringComparison.OrdinalIgnoreCase) && e.Type == SyncEventType.ConflictDetected)
            .ToList();

        var conflicts = conflictEvents
            .Select(e => new TeamMemoryConflict
            {
                MemoryId = Path.GetFileName(e.FilePath),
                LocalVersion = "local",
                RemoteVersion = "remote",
                ConflictType = ConflictType.ContentMismatch,
                DetectedAt = e.Timestamp
            })
            .ToImmutableList();

        var lastSync = _syncHistory
            .Where(e => e.FilePath.StartsWith(teamPath, StringComparison.OrdinalIgnoreCase))
            .MaxBy(e => e.Timestamp);

        return new TeamSyncStatus
        {
            TeamId = teamId,
            LastSyncAt = lastSync?.Timestamp,
            IsWatching = IsRunning,
            SyncedMemoryCount = localCount,
            HasConflicts = conflicts.Count > 0,
            Conflicts = conflicts
        };
    }

    protected override async ValueTask HandleAsync(ITeamMemorySyncCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case StartSyncCmd start:
                if (_startPipeline is not null)
                    await StartViaPipelineAsync(ct).ConfigureAwait(false);
                else
                    await StartDirectAsync(ct).ConfigureAwait(false);
                start.Tcs.TrySetResult();
                break;

            case StopSyncCmd stop:
                StopDirect();
                stop.Tcs.TrySetResult();
                break;

            case SyncCmd sync:
                if (sync.FilePath != null)
                    await SyncSingleFileAsync(sync.FilePath, ct).ConfigureAwait(false);
                else
                    await SyncAllFilesAsync(ct).ConfigureAwait(false);
                sync.Tcs?.TrySetResult();
                break;

            case ResolveConflictCmd resolve:
                var result = await ResolveConflictDirectAsync(resolve.FilePath, resolve.Resolution, ct).ConfigureAwait(false);
                resolve.Tcs.TrySetResult(result);
                break;

            case FileChangedCmd fileChanged:
                var syncEvent = new MemorySyncEvent
                {
                    EventId = Guid.NewGuid().ToString("N")[..8],
                    FilePath = fileChanged.FilePath,
                    Type = SyncEventType.LocalChanged,
                    Timestamp = _clock.GetUtcNow(),
                    ContentHash = ComputeFileHash(_fs, fileChanged.FilePath)
                };
                EnqueueEvent(syncEvent);
                if (_options.EnableAutoSync)
                {
                    await SyncSingleFileAsync(fileChanged.FilePath, ct).ConfigureAwait(false);
                }
                break;

            case FileDeletedCmd fileDeleted:
                _localEntries.TryRemove(fileDeleted.FilePath, out _);
                EnqueueEvent(new MemorySyncEvent
                {
                    EventId = Guid.NewGuid().ToString("N")[..8],
                    FilePath = fileDeleted.FilePath,
                    Type = SyncEventType.LocalChanged,
                    Timestamp = _clock.GetUtcNow()
                });
                break;

            case FileRenamedCmd fileRenamed:
                _localEntries.TryRemove(fileRenamed.OldPath, out _);
                EnqueueEvent(new MemorySyncEvent
                {
                    EventId = Guid.NewGuid().ToString("N")[..8],
                    FilePath = fileRenamed.NewPath,
                    Type = SyncEventType.LocalChanged,
                    Timestamp = _clock.GetUtcNow(),
                    ContentHash = ComputeFileHash(_fs, fileRenamed.NewPath)
                });
                break;
        }
    }

    protected override void OnConsumerError(Exception ex)
    {
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _disposeCts.Cancel();
        _syncTimer.Dispose();
        _watcher?.Dispose();
        DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(5));
        _disposeCts.Dispose();
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0) return;
        _disposeCts.Cancel();
        _syncTimer.Dispose();
        _watcher?.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
        _disposeCts.Dispose();
    }
}
