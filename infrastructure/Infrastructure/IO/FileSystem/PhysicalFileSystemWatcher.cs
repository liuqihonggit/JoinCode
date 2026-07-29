namespace IO.FileSystem;

/// <summary>
/// 物理文件系统监视器 — 委托给 System.IO.FileSystemWatcher + 内置防抖 + 内部写入过滤
/// </summary>
public sealed class PhysicalFileSystemWatcher : IFileSystemWatcher
{
    private readonly FileSystemWatcher _inner;
    private readonly DebounceTracker _debounce = new();
    private bool _disposed;

    public PhysicalFileSystemWatcher(string path, string filter = "*.*")
    {
        _inner = new FileSystemWatcher(path, filter);
        _inner.Changed += OnChanged;
        _inner.Created += OnCreated;
        _inner.Deleted += OnDeleted;
        _inner.Renamed += OnRenamed;
    }

    public string Path
    {
        get => _inner.Path;
        set => _inner.Path = value;
    }

    public string Filter
    {
        get => _inner.Filter;
        set => _inner.Filter = value;
    }

    public ICollection<string> Filters => _inner.Filters;

    public bool IncludeSubdirectories
    {
        get => _inner.IncludeSubdirectories;
        set => _inner.IncludeSubdirectories = value;
    }

    public NotifyFilters NotifyFilter
    {
        get => _inner.NotifyFilter;
        set => _inner.NotifyFilter = value;
    }

    public bool EnableRaisingEvents
    {
        get => _inner.EnableRaisingEvents;
        set => _inner.EnableRaisingEvents = value;
    }

    public TimeSpan DebounceInterval
    {
        get => _debounce.DebounceInterval;
        set => _debounce.DebounceInterval = value;
    }

    public int InternalWriteWindowMs
    {
        get => _debounce.InternalWriteWindowMs;
        set => _debounce.InternalWriteWindowMs = value;
    }

    public event EventHandler<FileChangedEventArgs>? Changed;
    public event EventHandler<FileChangedEventArgs>? Created;
    public event EventHandler<FileChangedEventArgs>? Deleted;
    public event EventHandler<FileRenamedEventArgs>? Renamed;

    public event EventHandler<FileChangedEventArgs>? DebouncedChanged;
    public event EventHandler<FileChangedEventArgs>? DebouncedCreated;
    public event EventHandler<FileChangedEventArgs>? DebouncedDeleted;
    public event EventHandler<FileRenamedEventArgs>? DebouncedRenamed;

    public void MarkInternalWrite(string filePath) => _debounce.MarkInternalWrite(filePath);

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (_debounce.ConsumeInternalWrite(e.FullPath)) return;
        var args = FromArgs(e);
        Changed?.Invoke(this, args);
        _debounce.ScheduleDebounce(e.FullPath, () => DebouncedChanged?.Invoke(this, args));
    }

    private void OnCreated(object sender, FileSystemEventArgs e)
    {
        if (_debounce.ConsumeInternalWrite(e.FullPath)) return;
        var args = FromArgs(e);
        Created?.Invoke(this, args);
        _debounce.ScheduleDebounce(e.FullPath, () => DebouncedCreated?.Invoke(this, args));
    }

    private void OnDeleted(object sender, FileSystemEventArgs e)
    {
        if (_debounce.ConsumeInternalWrite(e.FullPath)) return;
        var args = FromArgs(e);
        Deleted?.Invoke(this, args);
        _debounce.ScheduleDebounce(e.FullPath, () => DebouncedDeleted?.Invoke(this, args));
    }

    private void OnRenamed(object sender, RenamedEventArgs e)
    {
        if (_debounce.ConsumeInternalWrite(e.FullPath)) return;
        var args = FromRenamedArgs(e);
        Renamed?.Invoke(this, args);
        _debounce.ScheduleDebounce(e.FullPath, () => DebouncedRenamed?.Invoke(this, args));
    }

    private static FileChangedEventArgs FromArgs(FileSystemEventArgs e)
        => new() { ChangeType = e.ChangeType, FullPath = e.FullPath, Name = e.Name ?? string.Empty };

    private static FileRenamedEventArgs FromRenamedArgs(RenamedEventArgs e)
        => new()
        {
            ChangeType = e.ChangeType,
            FullPath = e.FullPath,
            Name = e.Name ?? string.Empty,
            OldFullPath = e.OldFullPath,
            OldName = e.OldName ?? string.Empty
        };

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;

        _inner.Changed -= OnChanged;
        _inner.Created -= OnCreated;
        _inner.Deleted -= OnDeleted;
        _inner.Renamed -= OnRenamed;
        _inner.Dispose();

        _debounce.Dispose();
    }
}
