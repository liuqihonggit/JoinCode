namespace JoinCode.Abstractions.Security.Auditing;

/// <summary>
/// 命令执行审计日志条目 — 记录每次命令执行的关键信息
/// </summary>
/// <param name="Timestamp">执行时间(UTC)</param>
/// <param name="Command">执行的命令文本</param>
/// <param name="DangerLevel">危险等级</param>
/// <param name="PermissionMode">权限模式</param>
/// <param name="Result">执行结果(Allowed/Rejected/Confirmed)</param>
/// <param name="Details">风险详情</param>
/// <param name="FilesChanged">文件系统变更列表(快照对比结果)</param>
public sealed record CommandExecutionAuditEntry(
    DateTimeOffset Timestamp,
    string Command,
    CommandDangerLevel DangerLevel,
    PermissionMode PermissionMode,
    string Result,
    string? Details = null,
    IReadOnlyList<FileChangeRecord>? FilesChanged = null);

/// <summary>
/// 文件变更记录 — 快照对比检测到的单个文件变更
/// </summary>
/// <param name="Path">文件相对路径</param>
/// <param name="ChangeType">变更类型(Created/Modified/Deleted)</param>
/// <param name="OldSize">变更前大小(字节),新建为 null</param>
/// <param name="NewSize">变更后大小(字节),删除为 null</param>
public sealed record FileChangeRecord(
    string Path,
    FileChangeType ChangeType,
    long? OldSize,
    long? NewSize);

/// <summary>
/// 文件变更类型
/// </summary>
public enum FileChangeType {
    /// <summary>新建文件</summary>
    [EnumValue("created")]
    Created,
    /// <summary>修改文件</summary>
    [EnumValue("modified")]
    Modified,
    /// <summary>删除文件</summary>
    [EnumValue("deleted")]
    Deleted
}