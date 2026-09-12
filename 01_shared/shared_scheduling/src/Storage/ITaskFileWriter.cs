namespace Core.Scheduling;

/// <summary>
/// 任务文件写入器接口
/// </summary>
public interface ITaskFileWriter
{
    /// <summary>
    /// 写入任务文件
    /// </summary>
    Task WriteAsync(string filePath, FileTaskMetadata metadata, CancellationToken cancellationToken = default);

    /// <summary>
    /// 原子写入任务文件（先写临时文件再重命名）
    /// </summary>
    Task WriteAtomicAsync(string filePath, FileTaskMetadata metadata, CancellationToken cancellationToken = default);
}

/// <summary>
/// 任务文件读取器接口
/// </summary>
public interface ITaskFileReader
{
    /// <summary>
    /// 读取任务文件
    /// </summary>
    Task<FileTaskMetadata?> ReadAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取目录中的所有任务
    /// </summary>
    Task<List<FileTaskMetadata>> ReadAllAsync(string directoryPath, CancellationToken cancellationToken = default);
}
