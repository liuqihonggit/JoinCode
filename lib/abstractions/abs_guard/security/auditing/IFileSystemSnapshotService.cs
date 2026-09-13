namespace JoinCode.Abstractions.Security.Auditing;

/// <summary>
/// 文件系统快照接口 — 在命令执行前后捕获文件系统状态,对比检测变更
/// <para>
/// 用法:
/// <list type="bullet">
/// <item>命令执行前调用 <see cref="Capture"/> 获取快照</item>
/// <item>命令执行后调用 <see cref="Compare"/> 对比快照</item>
/// <item>将变更结果附加到 <see cref="CommandExecutionAuditEntry"/></item>
/// </list>
/// </para>
/// </summary>
public interface IFileSystemSnapshotService
{
    /// <summary>
    /// 捕获指定目录的文件系统快照
    /// </summary>
    /// <param name="directoryPath">目录路径</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>文件系统快照</returns>
    Task<FileSystemSnapshot> CaptureAsync(string directoryPath, CancellationToken ct = default);

    /// <summary>
    /// 对比两个快照,检测文件变更
    /// </summary>
    /// <param name="before">执行前快照</param>
    /// <param name="after">执行后快照</param>
    /// <returns>文件变更列表</returns>
    IReadOnlyList<FileChangeRecord> Compare(FileSystemSnapshot before, FileSystemSnapshot after);
}

/// <summary>
/// 文件系统快照 — 记录某个时刻的文件系统状态
/// </summary>
/// <param name="DirectoryPath">快照目录路径</param>
/// <param name="CapturedAt">快照时间(UTC)</param>
/// <param name="Entries">文件条目集合(相对路径 → 文件元数据)</param>
public sealed record FileSystemSnapshot(
    string DirectoryPath,
    DateTimeOffset CapturedAt,
    FrozenDictionary<string, FileSnapshotEntry> Entries);

/// <summary>
/// 文件元数据条目
/// </summary>
/// <param name="RelativePath">相对目录的路径</param>
/// <param name="Size">文件大小(字节)</param>
/// <param name="LastWriteTimeUtc">最后修改时间(UTC)</param>
public sealed record FileSnapshotEntry(
    string RelativePath,
    long Size,
    DateTimeOffset LastWriteTimeUtc);
