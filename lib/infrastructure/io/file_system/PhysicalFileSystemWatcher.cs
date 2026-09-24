namespace IO.FileSystem;

/// <summary>
/// 物理文件系统监视器 — 委托给 System.IO.FileSystemWatcher + 内置防抖 + 内部写入过滤
/// </summary>
public sealed class PhysicalFileSystemWatcher : IFileSystemWatcher {
    private readonly FileSystemWatcher _inner;
    private readonly DebounceTracker _debounce = new();
    private int _disposed;

    /// <summary>
    /// 构造物理文件系统监视器
    /// </summary>
    /// <param name="path">监视目录路径</param>
    /// <param name="filter">文件名筛选器,默认 "*.*"</param>
    public PhysicalFileSystemWatcher(string path, string filter = "*.*") {
        _inner = new FileSystemWatcher(path, filter);
        _inner.Changed += OnChanged;
        _inner.Created += OnCreated;
        _inner.Deleted += OnDeleted;
        _inner.Renamed += OnRenamed;
    }

    /// <inheritdoc/>
    public string Path {
        get => _inner.Path;
        set => _inner.Path = value;
    }

    /// <inheritdoc/>
    public string Filter {
        get => _inner.Filter;
        set => _inner.Filter = value;
    }

    /// <inheritdoc/>
    public ICollection<string> Filters => _inner.Filters;

    /// <inheritdoc/>
    public bool IncludeSubdirectories {
        get => _inner.IncludeSubdirectories;
        set => _inner.IncludeSubdirectories = value;
    }

    /// <inheritdoc/>
    public NotifyFilters NotifyFilter {
        get => _inner.NotifyFilter;
        set => _inner.NotifyFilter = value;
    }

    /// <inheritdoc/>
    public bool EnableRaisingEvents {
        get => _inner.EnableRaisingEvents;
        set => _inner.EnableRaisingEvents = value;
    }

    /// <inheritdoc/>
    public TimeSpan DebounceInterval {
        get => _debounce.DebounceInterval;
        set => _debounce.DebounceInterval = value;
    }

    /// <inheritdoc/>
    public int InternalWriteWindowMs {
        get => _debounce.InternalWriteWindowMs;
        set => _debounce.InternalWriteWindowMs = value;
    }

    /// <inheritdoc/>
    public event EventHandler<FileChangedEventArgs>? Changed;
    /// <inheritdoc/>
    public event EventHandler<FileChangedEventArgs>? Created;
    /// <inheritdoc/>
    public event EventHandler<FileChangedEventArgs>? Deleted;
    /// <inheritdoc/>
    public event EventHandler<FileRenamedEventArgs>? Renamed;

    /// <inheritdoc/>
    public event EventHandler<FileChangedEventArgs>? DebouncedChanged;
    /// <inheritdoc/>
    public event EventHandler<FileChangedEventArgs>? DebouncedCreated;
    /// <inheritdoc/>
    public event EventHandler<FileChangedEventArgs>? DebouncedDeleted;
    /// <inheritdoc/>
    public event EventHandler<FileRenamedEventArgs>? DebouncedRenamed;

    /// <inheritdoc/>
    public void MarkInternalWrite(string filePath) => _debounce.MarkInternalWrite(filePath);

    private async void OnChanged(object sender, FileSystemEventArgs e) {
        try {
            if (_debounce.ConsumeInternalWrite(e.FullPath)) return;
            var args = FromArgs(e);
            Changed?.Invoke(this, args);
            await _debounce.ScheduleDebounce(e.FullPath, () => DebouncedChanged?.Invoke(this, args)).ConfigureAwait(false);
        } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[PhysicalFileSystemWatcher] {ex}"); }
    }

    private async void OnCreated(object sender, FileSystemEventArgs e) {
        try {
            if (_debounce.ConsumeInternalWrite(e.FullPath)) return;
            var args = FromArgs(e);
            Created?.Invoke(this, args);
            await _debounce.ScheduleDebounce(e.FullPath, () => DebouncedCreated?.Invoke(this, args)).ConfigureAwait(false);
        } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[PhysicalFileSystemWatcher] {ex}"); }
    }

    private async void OnDeleted(object sender, FileSystemEventArgs e) {
        try {
            if (_debounce.ConsumeInternalWrite(e.FullPath)) return;
            var args = FromArgs(e);
            Deleted?.Invoke(this, args);
            await _debounce.ScheduleDebounce(e.FullPath, () => DebouncedDeleted?.Invoke(this, args)).ConfigureAwait(false);
        } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[PhysicalFileSystemWatcher] {ex}"); }
    }

    private async void OnRenamed(object sender, RenamedEventArgs e) {
        try {
            if (_debounce.ConsumeInternalWrite(e.FullPath)) return;
            var args = FromRenamedArgs(e);
            Renamed?.Invoke(this, args);
            await _debounce.ScheduleDebounce(e.FullPath, () => DebouncedRenamed?.Invoke(this, args)).ConfigureAwait(false);
        } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"[PhysicalFileSystemWatcher] {ex}"); }
    }

    private static FileChangedEventArgs FromArgs(FileSystemEventArgs e)
        => new() { ChangeType = e.ChangeType, FullPath = e.FullPath, Name = e.Name ?? string.Empty };

    private static FileRenamedEventArgs FromRenamedArgs(RenamedEventArgs e)
        => new() {
            ChangeType = e.ChangeType,
            FullPath = e.FullPath,
            Name = e.Name ?? string.Empty,
            OldFullPath = e.OldFullPath,
            OldName = e.OldName ?? string.Empty
        };

    /// <summary>
    /// 释放内部 FileSystemWatcher 与防抖跟踪器资源
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _inner.Changed -= OnChanged;
        _inner.Created -= OnCreated;
        _inner.Deleted -= OnDeleted;
        _inner.Renamed -= OnRenamed;
        _inner.Dispose();

        _debounce.Dispose();
    }

    /// <summary>
    /// 异步释放内部 FileSystemWatcher 与防抖跟踪器资源
    /// </summary>
    public ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }
}