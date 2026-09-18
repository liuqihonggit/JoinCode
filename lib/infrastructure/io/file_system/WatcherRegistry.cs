namespace IO.FileSystem;

/// <summary>
/// 文件系统监视器注册表 — 管理 watcher 注册/注销/通知
/// 从 InMemoryFileSystem 提取,降低大类字段数和 watcher 通知逻辑复杂度
/// </summary>
internal sealed class WatcherRegistry
{
    private readonly List<InMemoryFileSystemWatcher> _watchers = [];
    private readonly AsyncLock _watchersLock = new("InMemoryFileSystem.Watchers");

    /// <summary>注册 watcher — 由 InMemoryFileSystemWatcher 内部调用</summary>
    public void Register(InMemoryFileSystemWatcher watcher)
    {
        using (_watchersLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_watchersLock.Name}' 等待超时")) _watchers.Add(watcher);
    }

    /// <summary>注销 watcher — 由 InMemoryFileSystemWatcher.Dispose 内部调用</summary>
    public void Unregister(InMemoryFileSystemWatcher watcher)
    {
        using (_watchersLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_watchersLock.Name}' 等待超时")) _watchers.Remove(watcher);
    }

    /// <summary>通知所有 watcher 文件变更</summary>
    public void NotifyChanged(string fullPath, WatcherChangeTypes changeType)
    {
        List<InMemoryFileSystemWatcher> snapshot;
        using (_watchersLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_watchersLock.Name}' 等待超时")) snapshot = [.. _watchers];
        foreach (var watcher in snapshot)
            watcher.OnFileChanged(fullPath, changeType);
    }

    /// <summary>通知所有 watcher 文件重命名</summary>
    public void NotifyRenamed(string oldFullPath, string newFullPath)
    {
        List<InMemoryFileSystemWatcher> snapshot;
        using (_watchersLock.TryLock() ?? throw new System.TimeoutException($"锁 '{_watchersLock.Name}' 等待超时")) snapshot = [.. _watchers];
        foreach (var watcher in snapshot)
            watcher.OnFileRenamed(oldFullPath, newFullPath);
    }
}
