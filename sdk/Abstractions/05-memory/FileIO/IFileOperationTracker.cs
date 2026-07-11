
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 文件操作记录
/// </summary>
public sealed class FileOperationEntry
{
    public required string FilePath { get; init; }
    public required FileOperationType OperationType { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 文件操作追踪器接口 - 记录会话中的文件读写编辑操作
/// </summary>
public interface IFileOperationTracker
{
    /// <summary>
    /// 记录文件操作
    /// </summary>
    void Track(string filePath, FileOperationType operationType);

    /// <summary>
    /// 获取所有文件操作记录
    /// </summary>
    IReadOnlyList<FileOperationEntry> GetAllEntries();

    /// <summary>
    /// 获取所有被操作过的文件路径（去重）
    /// </summary>
    IReadOnlyList<string> GetOperatedFilePaths();

    /// <summary>
    /// 清除所有记录
    /// </summary>
    void Clear();
}
