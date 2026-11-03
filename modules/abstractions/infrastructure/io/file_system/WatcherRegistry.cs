namespace IO.FileSystem;

/// <summary>
/// 文件系统监视器注册表 — 管理 watcher 注册/注销/通知
/// 从 InMemoryFileSystem 提取,降低大类字段数和 watcher 通知逻辑复杂度
/// </summary>
internal sealed class WatcherRegistry {
    private ImmutableList<InMemoryFileSystemWatcher> _watchers = ImmutableList<InMemoryFileSystemWatcher>.Empty;

    /// <summary>注册 watcher — 由 InMemoryFileSystemWatcher 内部调用</summary>
    public void Register(InMemoryFileSystemWatcher watcher) {
        ImmutableInterlocked.Update(ref _watchers, list => list.Add(watcher));
    }

    /// <summary>注销 watcher — 由 InMemoryFileSystemWatcher.Dispose 内部调用</summary>
    public void Unregister(InMemoryFileSystemWatcher watcher) {
        ImmutableInterlocked.Update(ref _watchers, list => list.Remove(watcher));
    }

    /// <summary>通知所有 watcher 文件变更</summary>
    public void NotifyChanged(string fullPath, WatcherChangeTypes changeType) {
        var snapshot = Volatile.Read(ref _watchers);
        foreach (var watcher in snapshot)
            watcher.OnFileChanged(fullPath, changeType);
    }

    /// <summary>通知所有 watcher 文件重命名</summary>
    public void NotifyRenamed(string oldFullPath, string newFullPath) {
        var snapshot = Volatile.Read(ref _watchers);
        foreach (var watcher in snapshot)
            watcher.OnFileRenamed(oldFullPath, newFullPath);
    }
}
