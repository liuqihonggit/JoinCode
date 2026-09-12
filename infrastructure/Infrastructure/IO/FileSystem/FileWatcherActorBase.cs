namespace IO.FileSystem;

/// <summary>
/// 文件监控 Actor 基类 — 封装 IFileSystemWatcher 生命周期 + 防抖 + 内部写入过滤 + 事件→命令转换。
/// <para>子类继承后只需 override HandleFileChangedAsync 处理真正的文件变更,所有并发安全由基类保证。</para>
/// <para>状态独占: _watcher 和 _internalWrites 由 Consumer 线程独占访问,无需锁。</para>
/// <para>事件回调: OnWatcherChanged/OnWatcherRenamed 不访问状态,只 TrySend 投递命令到邮箱。</para>
/// <para>死循环防护: 写前 TrySend(MarkInternalWriteCmd) → Consumer 记录时间戳 → 文件变更事件 → TrySend(FileChangedCmd) → Consumer 过滤窗口内变更。</para>
/// <para>背压: 有界邮箱 + DropOldest 策略,防止批量变更刷爆(默认容量 1000)。</para>
/// </summary>
public abstract class FileWatcherActorBase : ActorBase<FileWatcherCommand, Unit>
{
    private IFileSystemWatcher? _watcher;
    private readonly Dictionary<string, DateTimeOffset> _internalWrites = new(StringComparer.Ordinal);
    private TimeSpan _internalWriteWindow = TimeSpan.FromSeconds(5);

    /// <summary>文件系统抽象 — 子类可访问用于路径检查等</summary>
    protected readonly IFileSystem FileSystem;

    /// <summary>
    /// 构造文件监控 Actor。
    /// </summary>
    /// <param name="fileSystem">文件系统抽象(生产用 PhysicalFileSystem,测试用 InMemoryFileSystem)</param>
    /// <param name="mailboxCapacity">Actor 邮箱容量(默认 1000,满时丢弃最旧命令)</param>
    protected FileWatcherActorBase(IFileSystem fileSystem, int mailboxCapacity = 1000)
        : base(new ActorBackpressure(mailboxCapacity, BoundedChannelFullMode.DropOldest), null)
    {
        FileSystem = fileSystem;
    }

    /// <summary>内部写入过滤窗口 — 窗口内的变更视为自身写入回声,丢弃(默认 5s)</summary>
    protected TimeSpan InternalWriteWindow
    {
        get => _internalWriteWindow;
        set => _internalWriteWindow = value;
    }

    /// <summary>
    /// 子类实现文件变更处理 — 由 Consumer 线程串行调用,此方法内访问实例可变状态无需锁。
    /// <para>此方法仅在通过内部写入过滤后才调用(真正的外部变更)。</para>
    /// </summary>
    protected abstract ValueTask HandleFileChangedAsync(string filePath, WatcherChangeTypes kind, DateTimeOffset timestamp, CancellationToken ct);

    /// <summary>子类重写处理文件重命名 — 默认空实现</summary>
    protected virtual ValueTask HandleFileRenamedAsync(string oldPath, string newPath, DateTimeOffset timestamp, CancellationToken ct) => ValueTask.CompletedTask;

    /// <summary>
    /// 子类重写处理自定义命令 — 通用命令(FileChangedCmd 等)由基类处理,自定义命令通过此方法扩展。
    /// </summary>
    protected virtual ValueTask HandleCustomCommandAsync(FileWatcherCommand cmd, CancellationToken ct) => ValueTask.CompletedTask;

    /// <summary>
    /// 命令分发 — 由 Consumer 线程串行调用,所有状态访问无需锁。
    /// </summary>
    protected override ValueTask HandleAsync(FileWatcherCommand cmd, CancellationToken ct)
    {
        return cmd switch
        {
            FileChangedCmd c => HandleFileChangedCoreAsync(c, ct),
            FileRenamedCmd c => HandleFileRenamedAsync(c.OldPath, c.NewPath, c.Timestamp, ct),
            MarkInternalWriteCmd c => HandleMarkInternalWriteCore(c.FilePath),
            FileWatcherStartCmd c => StartWatcherCoreAsync(c.Path, c.Filter, c.DebounceInterval, c.IncludeSubdirectories, c.NotifyFilter),
            FileWatcherStopCmd => StopWatcherCoreAsync(),
            _ => HandleCustomCommandAsync(cmd, ct)
        };
    }

    /// <summary>
    /// 标记内部写入 — 在自身写入文件前调用,投递 MarkInternalWriteCmd 到邮箱。
    /// <para>此方法可从任意线程调用(只 TrySend,不访问状态)。</para>
    /// </summary>
    public void MarkInternalWrite(string filePath) => TrySend(new MarkInternalWriteCmd(filePath));

    private ValueTask HandleFileChangedCoreAsync(FileChangedCmd cmd, CancellationToken ct)
    {
        if (ConsumeInternalWrite(cmd.FilePath))
            return ValueTask.CompletedTask;
        return HandleFileChangedAsync(cmd.FilePath, cmd.Kind, cmd.Timestamp, ct);
    }

    private ValueTask HandleMarkInternalWriteCore(string filePath)
    {
        _internalWrites[filePath] = DateTimeOffset.UtcNow;
        PruneExpiredInternalWrites();
        return ValueTask.CompletedTask;
    }

    private bool ConsumeInternalWrite(string filePath)
    {
        if (!_internalWrites.TryGetValue(filePath, out var timestamp))
            return false;
        _internalWrites.Remove(filePath);
        return DateTimeOffset.UtcNow - timestamp <= _internalWriteWindow;
    }

    /// <summary>清理过期的内部写入记录 — 防止字典无限增长</summary>
    private void PruneExpiredInternalWrites()
    {
        if (_internalWrites.Count <= 64)
            return;
        var now = DateTimeOffset.UtcNow;
        var expired = new List<string>(_internalWrites.Count / 2);
        foreach (var kvp in _internalWrites)
        {
            if (now - kvp.Value > _internalWriteWindow)
                expired.Add(kvp.Key);
        }
        foreach (var key in expired)
            _internalWrites.Remove(key);
    }

    private ValueTask StartWatcherCoreAsync(string path, string? filter, TimeSpan debounceInterval, bool includeSubdirectories, NotifyFilters notifyFilter)
    {
        _watcher?.Dispose();
        _watcher = FileSystem.Watch(path, filter ?? "*.*");
        _watcher.IncludeSubdirectories = includeSubdirectories;
        _watcher.NotifyFilter = notifyFilter;
        _watcher.DebounceInterval = debounceInterval;
        _watcher.EnableRaisingEvents = true;
        _watcher.DebouncedChanged += OnWatcherChanged;
        _watcher.DebouncedCreated += OnWatcherChanged;
        _watcher.DebouncedDeleted += OnWatcherChanged;
        _watcher.DebouncedRenamed += OnWatcherRenamed;
        return ValueTask.CompletedTask;
    }

    private ValueTask StopWatcherCoreAsync()
    {
        if (_watcher is null)
            return ValueTask.CompletedTask;
        _watcher.DebouncedChanged -= OnWatcherChanged;
        _watcher.DebouncedCreated -= OnWatcherChanged;
        _watcher.DebouncedDeleted -= OnWatcherChanged;
        _watcher.DebouncedRenamed -= OnWatcherRenamed;
        _watcher.Dispose();
        _watcher = null;
        return ValueTask.CompletedTask;
    }

    /// <summary>watcher 变更事件 → 投递 FileChangedCmd 到邮箱(不访问状态,可从任意线程调用)</summary>
    private void OnWatcherChanged(object? sender, FileChangedEventArgs e)
        => TrySend(new FileChangedCmd(e.FullPath, e.ChangeType, DateTimeOffset.UtcNow));

    /// <summary>watcher 重命名事件 → 投递 FileRenamedCmd 到邮箱(不访问状态,可从任意线程调用)</summary>
    private void OnWatcherRenamed(object? sender, FileRenamedEventArgs e)
        => TrySend(new FileRenamedCmd(e.OldFullPath, e.FullPath, DateTimeOffset.UtcNow));

    /// <summary>
    /// 释放 Actor — 先发停止命令让 Consumer 停 watcher,再等待 Consumer 退出,最后 fallback 释放。
    /// </summary>
    public override async ValueTask DisposeAsync()
    {
        TrySend(new FileWatcherStopCmd());
        await base.DisposeAsync().ConfigureAwait(false);
        _watcher?.Dispose();
        _watcher = null;
    }
}
