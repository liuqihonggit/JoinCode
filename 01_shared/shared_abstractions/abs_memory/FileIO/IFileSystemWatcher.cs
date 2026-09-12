namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 文件系统监视器抽象 — 对齐 System.IO.FileSystemWatcher
/// <para>生产环境: PhysicalFileSystemWatcher (委托给 System.IO.FileSystemWatcher)</para>
/// <para>测试环境: InMemoryFileSystemWatcher (文件操作触发事件)</para>
/// </summary>
public interface IFileSystemWatcher : IDisposable
{
    /// <summary>获取或设置要监视的目录路径</summary>
    string Path { get; set; }

    /// <summary>获取或设置筛选字符串 — 对齐 FileSystemWatcher.Filter</summary>
    string Filter { get; set; }

    /// <summary>获取筛选集合 — 对齐 FileSystemWatcher.Filters (net6+)</summary>
    ICollection<string> Filters { get; }

    /// <summary>获取或设置是否监视子目录</summary>
    bool IncludeSubdirectories { get; set; }

    /// <summary>获取或设置通知筛选条件 — 对齐 NotifyFilters</summary>
    NotifyFilters NotifyFilter { get; set; }

    /// <summary>获取或设置是否启用事件</summary>
    bool EnableRaisingEvents { get; set; }

    /// <summary>文件变更事件 — 对齐 FileSystemWatcher.Changed</summary>
    event EventHandler<FileChangedEventArgs>? Changed;

    /// <summary>文件创建事件 — 对齐 FileSystemWatcher.Created</summary>
    event EventHandler<FileChangedEventArgs>? Created;

    /// <summary>文件删除事件 — 对齐 FileSystemWatcher.Deleted</summary>
    event EventHandler<FileChangedEventArgs>? Deleted;

    /// <summary>文件重命名事件 — 对齐 FileSystemWatcher.Renamed</summary>
    event EventHandler<FileRenamedEventArgs>? Renamed;

    // === 内置防抖 ===

    /// <summary>
    /// 获取或设置防抖间隔 — 默认 500ms，设为 Zero 或负值禁用防抖
    /// <para>防抖粒度: 按文件路径独立 Timer，避免全局单 Timer 丢失事件</para>
    /// </summary>
    TimeSpan DebounceInterval { get; set; }

    /// <summary>
    /// 防抖后的文件变更事件 — 每个文件路径独立防抖，到期后触发
    /// <para>与 Changed 事件互斥: 消费方应选择订阅 Changed（原始）或 DebouncedChanged（防抖后）</para>
    /// </summary>
    event EventHandler<FileChangedEventArgs>? DebouncedChanged;

    /// <summary>防抖后的文件创建事件</summary>
    event EventHandler<FileChangedEventArgs>? DebouncedCreated;

    /// <summary>防抖后的文件删除事件</summary>
    event EventHandler<FileChangedEventArgs>? DebouncedDeleted;

    /// <summary>防抖后的文件重命名事件</summary>
    event EventHandler<FileRenamedEventArgs>? DebouncedRenamed;

    // === 内部写入过滤 ===

    /// <summary>
    /// 标记内部写入 — 在自身写入文件前调用，窗口期内该文件变更不触发事件
    /// </summary>
    void MarkInternalWrite(string filePath);

    /// <summary>
    /// 获取或设置内部写入过滤窗口 — 默认 5000ms
    /// </summary>
    int InternalWriteWindowMs { get; set; }
}

/// <summary>
/// 文件变更事件参数 — 对齐 System.IO.FileSystemEventArgs
/// </summary>
public class FileChangedEventArgs : EventArgs
{
    public WatcherChangeTypes ChangeType { get; init; }
    public string FullPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
}

/// <summary>
/// 文件重命名事件参数 — 对齐 System.IO.RenamedEventArgs
/// </summary>
public sealed class FileRenamedEventArgs : FileChangedEventArgs
{
    public string OldFullPath { get; init; } = string.Empty;
    public string OldName { get; init; } = string.Empty;
}
