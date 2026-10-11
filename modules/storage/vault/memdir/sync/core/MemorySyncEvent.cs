
namespace Memdir.Sync;

/// <summary>
/// 记忆同步事件 — 描述一次文件同步操作的元数据
/// </summary>
public sealed class MemorySyncEvent {
    /// <summary>事件唯一标识</summary>
    public required string EventId { get; init; }
    /// <summary>涉及的文件路径</summary>
    public required string FilePath { get; init; }
    /// <summary>同步事件类型</summary>
    public required SyncEventType Type { get; init; }
    /// <summary>事件发生时间</summary>
    public required DateTime Timestamp { get; init; }
    /// <summary>内容哈希值,用于变更检测</summary>
    public string? ContentHash { get; init; }
    /// <summary>冲突解决方式(若发生冲突)</summary>
    public SyncConflictResolution? ConflictResolution { get; init; }
    /// <summary>错误信息(若事件为错误类型)</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// 同步事件类型枚举
/// </summary>
public enum SyncEventType {
    /// <summary>本地文件发生变更。</summary>
    [EnumValue("localChanged")]
    LocalChanged,
    /// <summary>远程文件发生变更。</summary>
    [EnumValue("remoteChanged")]
    RemoteChanged,
    /// <summary>文件已成功同步。</summary>
    [EnumValue("synced")]
    Synced,
    /// <summary>检测到同步冲突。</summary>
    [EnumValue("conflictDetected")]
    ConflictDetected,
    /// <summary>同步冲突已解决。</summary>
    [EnumValue("conflictResolved")]
    ConflictResolved,
    /// <summary>同步过程中发生错误。</summary>
    [EnumValue("error")]
    Error
}