namespace IO.FileSystem;

/// <summary>
/// 文件监控 Actor 命令基类 — 所有文件监控命令的根类型。
/// <para>通用命令(FileChangedCmd/FileRenamedCmd/MarkInternalWriteCmd/FileWatcherStartCmd/FileWatcherStopCmd)由 FileWatcherActorBase 处理。</para>
/// <para>子类自定义命令继承此基类,通过 HandleCustomCommandAsync 扩展。</para>
/// </summary>
public abstract record FileWatcherCommand;

/// <summary>
/// 文件变更命令 — 文件被修改/创建/删除时由 watcher 事件投递到 Actor 邮箱。
/// <para>Consumer 处理时先经过 ConsumeInternalWrite 过滤,在窗口内的内部写入被丢弃(防回声)。</para>
/// </summary>
public sealed record FileChangedCmd(string FilePath, WatcherChangeTypes Kind, DateTimeOffset Timestamp) : FileWatcherCommand;

/// <summary>文件重命名命令 — 文件被重命名时由 watcher 事件投递到 Actor 邮箱</summary>
public sealed record FileRenamedCmd(string OldPath, string NewPath, DateTimeOffset Timestamp) : FileWatcherCommand;

/// <summary>
/// 标记内部写入命令 — 自身写入文件前投递到 Actor 邮箱,Consumer 记录时间戳。
/// <para>后续文件变更事件到达时,在 InternalWriteWindow 内的变更被丢弃,防止自身写入触发回声死循环。</para>
/// </summary>
public sealed record MarkInternalWriteCmd(string FilePath) : FileWatcherCommand;

/// <summary>启动文件监控命令 — Consumer 创建 IFileSystemWatcher 并订阅防抖事件</summary>
public sealed record FileWatcherStartCmd(string Path, string? Filter, TimeSpan DebounceInterval) : FileWatcherCommand;

/// <summary>停止文件监控命令 — Consumer 释放 IFileSystemWatcher</summary>
public sealed record FileWatcherStopCmd : FileWatcherCommand;
