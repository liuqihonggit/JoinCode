namespace JoinCode.Abstractions.LLM.Chat;

public sealed class FileSnapshot
{
    public required string Content { get; init; }
    public required DateTime Timestamp { get; init; }
    public required string FilePath { get; init; }
}

public sealed class FileHistoryService
{
    private readonly int _maxSnapshots;
    private readonly IFileSystem _fs;
    private readonly IClockService _clock;
    private readonly Dictionary<string, List<FileSnapshot>> _history = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public FileHistoryService(IFileSystem fs, int maxSnapshots = 100, IClockService? clock = null)
    {
        _fs = fs;
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSnapshots, 1);
        _maxSnapshots = maxSnapshots;
        _clock = clock ?? SystemClockService.Instance;
    }

    public async Task<bool> TrackEditAsync(string filePath, string originalContent, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalizedPath = Path.GetFullPath(filePath);

            if (!_history.TryGetValue(normalizedPath, out var snapshots))
            {
                snapshots = [];
                _history[normalizedPath] = snapshots;
            }

            snapshots.Add(new FileSnapshot
            {
                Content = originalContent,
                Timestamp = _clock.GetUtcNow(),
                FilePath = normalizedPath
            });

            while (snapshots.Count > _maxSnapshots)
            {
                // 从尾部保留 _maxSnapshots 个，避免 RemoveAt(0) 的 O(n) 移动
                var removeCount = snapshots.Count - _maxSnapshots;
                snapshots.RemoveRange(0, removeCount);
            }

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<FileSnapshot>> GetSnapshotsAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalizedPath = Path.GetFullPath(filePath);
            return _history.TryGetValue(normalizedPath, out var snapshots)
                ? snapshots.ToList()
                : [];
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<bool> RestoreAsync(string filePath, int snapshotIndex, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var normalizedPath = Path.GetFullPath(filePath);

            if (!_history.TryGetValue(normalizedPath, out var snapshots))
                return false;

            if (snapshotIndex < 0 || snapshotIndex >= snapshots.Count)
                return false;

            var snapshot = snapshots[snapshotIndex];

            if (!_fs.FileExists(normalizedPath))
                return false;

            await _fs.WriteAllTextAsync(normalizedPath, snapshot.Content, cancellationToken).ConfigureAwait(false);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _history.Clear();
        }
        finally
        {
            _lock.Release();
        }
    }
}
