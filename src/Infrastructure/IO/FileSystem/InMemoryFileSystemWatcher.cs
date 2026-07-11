namespace IO.FileSystem;

/// <summary>
/// 内存文件系统监视器 — InMemoryFileSystem 文件操作触发事件 + 内置防抖 + 内部写入过滤
/// </summary>
public sealed class InMemoryFileSystemWatcher : IFileSystemWatcher
{
    private readonly InMemoryFileSystem _fs;
    private readonly ConcurrentDictionary<string, Timer> _debounceTimers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, long> _internalWriteTimestamps = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    public InMemoryFileSystemWatcher(InMemoryFileSystem fs, string path, string filter = "*.*")
    {
        _fs = fs;
        Path = path;
        Filter = filter;
    }

    public string Path { get; set; } = string.Empty;
    public string Filter { get; set; } = "*.*";
    public ICollection<string> Filters { get; } = new List<string>();
    public bool IncludeSubdirectories { get; set; }
    public NotifyFilters NotifyFilter { get; set; } = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
    public bool EnableRaisingEvents { get; set; }
    public TimeSpan DebounceInterval { get; set; } = TimeSpan.FromMilliseconds(500);
    public int InternalWriteWindowMs { get; set; } = 5000;

    public event EventHandler<FileChangedEventArgs>? Changed;
    public event EventHandler<FileChangedEventArgs>? Created;
    public event EventHandler<FileChangedEventArgs>? Deleted;
    public event EventHandler<FileRenamedEventArgs>? Renamed;

    public event EventHandler<FileChangedEventArgs>? DebouncedChanged;
    public event EventHandler<FileChangedEventArgs>? DebouncedCreated;
    public event EventHandler<FileChangedEventArgs>? DebouncedDeleted;
    public event EventHandler<FileRenamedEventArgs>? DebouncedRenamed;

    public void MarkInternalWrite(string filePath)
    {
        var normalizedPath = System.IO.Path.GetFullPath(filePath);
        _internalWriteTimestamps[normalizedPath] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    private bool ConsumeInternalWrite(string filePath)
    {
        var normalizedPath = System.IO.Path.GetFullPath(filePath);
        if (_internalWriteTimestamps.TryRemove(normalizedPath, out var timestamp))
        {
            var elapsed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - timestamp;
            return elapsed < InternalWriteWindowMs;
        }
        return false;
    }

    internal void OnFileChanged(string fullPath, WatcherChangeTypes changeType)
    {
        if (!EnableRaisingEvents || _disposed) return;
        if (!MatchesWatch(fullPath)) return;
        if (ConsumeInternalWrite(fullPath)) return;

        var name = System.IO.Path.GetFileName(fullPath);
        var args = new FileChangedEventArgs { ChangeType = changeType, FullPath = fullPath, Name = name };

        switch (changeType)
        {
            case WatcherChangeTypes.Changed:
                Changed?.Invoke(this, args);
                ScheduleDebounce(fullPath, () => DebouncedChanged?.Invoke(this, args));
                break;
            case WatcherChangeTypes.Created:
                Created?.Invoke(this, args);
                ScheduleDebounce(fullPath, () => DebouncedCreated?.Invoke(this, args));
                break;
            case WatcherChangeTypes.Deleted:
                Deleted?.Invoke(this, args);
                ScheduleDebounce(fullPath, () => DebouncedDeleted?.Invoke(this, args));
                break;
        }
    }

    internal void OnFileRenamed(string oldFullPath, string newFullPath)
    {
        if (!EnableRaisingEvents || _disposed) return;
        if (!MatchesWatch(newFullPath)) return;
        if (ConsumeInternalWrite(newFullPath)) return;

        var args = new FileRenamedEventArgs
        {
            ChangeType = WatcherChangeTypes.Renamed,
            FullPath = newFullPath,
            Name = System.IO.Path.GetFileName(newFullPath),
            OldFullPath = oldFullPath,
            OldName = System.IO.Path.GetFileName(oldFullPath)
        };

        Renamed?.Invoke(this, args);
        ScheduleDebounce(newFullPath, () => DebouncedRenamed?.Invoke(this, args));
    }

    private void ScheduleDebounce(string filePath, Action fireAction)
    {
        var interval = DebounceInterval;
        if (interval <= TimeSpan.Zero)
        {
            fireAction();
            return;
        }

        if (_debounceTimers.TryRemove(filePath, out var existingTimer))
            existingTimer.Dispose();

        _debounceTimers[filePath] = new Timer(_ =>
        {
            _debounceTimers.TryRemove(filePath, out var timer);
            timer?.Dispose();
            if (!_disposed) fireAction();
        }, null, interval, Timeout.InfiniteTimeSpan);
    }

    private bool MatchesWatch(string fullPath)
    {
        var normalizedWatchPath = InMemoryFileSystem.NormalizePath(Path);
        var normalizedFullPath = InMemoryFileSystem.NormalizePath(fullPath);

        if (!IncludeSubdirectories)
        {
            var dir = System.IO.Path.GetDirectoryName(normalizedFullPath)?.Replace('\\', '/') ?? string.Empty;
            if (dir != normalizedWatchPath) return false;
        }
        else
        {
            if (!normalizedFullPath.StartsWith(normalizedWatchPath + "/", StringComparison.Ordinal)
                && normalizedFullPath != normalizedWatchPath)
                return false;
        }

        if (!MatchesFilter(System.IO.Path.GetFileName(normalizedFullPath))) return false;

        return true;
    }

    private bool MatchesFilter(string fileName)
    {
        var patterns = new List<string> { Filter };
        if (Filters.Count > 0) patterns.AddRange(Filters);

        foreach (var pattern in patterns)
        {
            if (MatchesGlob(fileName, pattern)) return true;
        }
        return false;
    }

    private static bool MatchesGlob(string fileName, string pattern)
    {
        if (pattern == "*.*") return true;
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(fileName, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _debounceTimers)
            kvp.Value.Dispose();
        _debounceTimers.Clear();
        _internalWriteTimestamps.Clear();

        _fs.UnregisterWatcher(this);
    }
}
