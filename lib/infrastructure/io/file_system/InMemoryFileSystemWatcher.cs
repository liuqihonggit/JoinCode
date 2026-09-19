using System.Threading;
namespace IO.FileSystem;

/// <summary>
/// 内存文件系统监视器 — InMemoryFileSystem 文件操作触发事件 + 内置防抖 + 内部写入过滤
/// </summary>
public sealed class InMemoryFileSystemWatcher : IFileSystemWatcher {
    private readonly InMemoryFileSystem _fs;
    private readonly DebounceTracker _debounce = new();
    private int _disposed;

    /// <summary>
    /// 构造内存文件系统监视器
    /// </summary>
    /// <param name="fs">所属内存文件系统</param>
    /// <param name="path">监视路径</param>
    /// <param name="filter">文件名筛选模式，默认 *.*</param>
    public InMemoryFileSystemWatcher(InMemoryFileSystem fs, string path, string filter = "*.*") {
        _fs = fs;
        Path = path;
        Filter = filter;
    }

    /// <summary>监视目录路径</summary>
    public string Path { get; set; } = string.Empty;
    /// <summary>文件名筛选模式</summary>
    public string Filter { get; set; } = "*.*";
    /// <summary>多文件名筛选模式集合</summary>
    public ICollection<string> Filters { get; } = new List<string>();
    /// <summary>是否包含子目录监视</summary>
    public bool IncludeSubdirectories { get; set; }
    /// <summary>监视变更类型过滤器</summary>
    public NotifyFilters NotifyFilter { get; set; } = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size;
    /// <summary>是否启用事件触发</summary>
    public bool EnableRaisingEvents { get; set; }
    /// <summary>防抖间隔</summary>
    public TimeSpan DebounceInterval {
        get => _debounce.DebounceInterval;
        set => _debounce.DebounceInterval = value;
    }

    /// <summary>内部写入过滤窗口（毫秒），窗口内的同路径事件视为内部写入而忽略</summary>
    public int InternalWriteWindowMs {
        get => _debounce.InternalWriteWindowMs;
        set => _debounce.InternalWriteWindowMs = value;
    }

    /// <summary>文件变更事件（防抖前）</summary>
    public event EventHandler<FileChangedEventArgs>? Changed;
    /// <summary>文件创建事件（防抖前）</summary>
    public event EventHandler<FileChangedEventArgs>? Created;
    /// <summary>文件删除事件（防抖前）</summary>
    public event EventHandler<FileChangedEventArgs>? Deleted;
    /// <summary>文件重命名事件（防抖前）</summary>
    public event EventHandler<FileRenamedEventArgs>? Renamed;

    /// <summary>文件变更事件（防抖后）</summary>
    public event EventHandler<FileChangedEventArgs>? DebouncedChanged;
    /// <summary>文件创建事件（防抖后）</summary>
    public event EventHandler<FileChangedEventArgs>? DebouncedCreated;
    /// <summary>文件删除事件（防抖后）</summary>
    public event EventHandler<FileChangedEventArgs>? DebouncedDeleted;
    /// <summary>文件重命名事件（防抖后）</summary>
    public event EventHandler<FileRenamedEventArgs>? DebouncedRenamed;

    /// <summary>
    /// 标记指定文件为内部写入，后续事件触发时在窗口内忽略
    /// </summary>
    /// <param name="filePath">文件路径</param>
    public void MarkInternalWrite(string filePath) => _debounce.MarkInternalWrite(filePath);

    /// <summary>
    /// 触发文件变更事件 — 由 InMemoryFileSystem 在文件变更时调用，经防抖和过滤后投递给订阅者
    /// </summary>
    /// <param name="fullPath">文件完整路径</param>
    /// <param name="changeType">变更类型</param>
    internal void OnFileChanged(string fullPath, WatcherChangeTypes changeType) {
        if (!EnableRaisingEvents || _disposed != 0) return;
        if (!MatchesWatch(fullPath)) return;
        if (_debounce.ConsumeInternalWrite(fullPath)) return;

        var name = System.IO.Path.GetFileName(fullPath);
        var args = new FileChangedEventArgs { ChangeType = changeType, FullPath = fullPath, Name = name };

        switch (changeType) {
            case WatcherChangeTypes.Changed:
            Changed?.Invoke(this, args);
            _debounce.ScheduleDebounce(fullPath, () => DebouncedChanged?.Invoke(this, args));
            break;
            case WatcherChangeTypes.Created:
            Created?.Invoke(this, args);
            _debounce.ScheduleDebounce(fullPath, () => DebouncedCreated?.Invoke(this, args));
            break;
            case WatcherChangeTypes.Deleted:
            Deleted?.Invoke(this, args);
            _debounce.ScheduleDebounce(fullPath, () => DebouncedDeleted?.Invoke(this, args));
            break;
        }
    }

    /// <summary>
    /// 触发文件重命名事件 — 由 InMemoryFileSystem 在文件重命名时调用，经防抖和过滤后投递给订阅者
    /// </summary>
    /// <param name="oldFullPath">原文件完整路径</param>
    /// <param name="newFullPath">新文件完整路径</param>
    internal void OnFileRenamed(string oldFullPath, string newFullPath) {
        if (!EnableRaisingEvents || _disposed != 0) return;
        if (!MatchesWatch(newFullPath)) return;
        if (_debounce.ConsumeInternalWrite(newFullPath)) return;

        var args = new FileRenamedEventArgs {
            ChangeType = WatcherChangeTypes.Renamed,
            FullPath = newFullPath,
            Name = System.IO.Path.GetFileName(newFullPath),
            OldFullPath = oldFullPath,
            OldName = System.IO.Path.GetFileName(oldFullPath)
        };

        Renamed?.Invoke(this, args);
        _debounce.ScheduleDebounce(newFullPath, () => DebouncedRenamed?.Invoke(this, args));
    }

    private bool MatchesWatch(string fullPath) {
        var normalizedWatchPath = InMemoryFileSystem.NormalizePath(Path);
        var normalizedFullPath = InMemoryFileSystem.NormalizePath(fullPath);

        if (!IncludeSubdirectories) {
            var dir = System.IO.Path.GetDirectoryName(normalizedFullPath)?.Replace('\\', '/') ?? string.Empty;
            if (dir != normalizedWatchPath) return false;
        } else {
            if (!normalizedFullPath.StartsWith(normalizedWatchPath + "/", StringComparison.Ordinal)
                && normalizedFullPath != normalizedWatchPath)
                return false;
        }

        if (!MatchesFilter(System.IO.Path.GetFileName(normalizedFullPath))) return false;

        return true;
    }

    private bool MatchesFilter(string fileName) {
        var patterns = new List<string> { Filter };
        if (Filters.Count > 0) patterns.AddRange(Filters);

        foreach (var pattern in patterns) {
            if (MatchesGlob(fileName, pattern)) return true;
        }
        return false;
    }

    private static bool MatchesGlob(string fileName, string pattern) {
        if (pattern == "*.*") return true;
        var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern)
            .Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(fileName, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    /// <summary>
    /// 释放监视器资源，从文件系统注销自身
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _debounce.Dispose();
        _fs.UnregisterWatcher(this);
    }
}