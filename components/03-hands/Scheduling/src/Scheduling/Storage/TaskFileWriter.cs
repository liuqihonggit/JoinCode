
namespace Core.Scheduling;

/// <summary>
/// 任务文件写入工具类
/// </summary>
[Register]
public sealed partial class TaskFileWriter : ITaskFileWriter
{
    [Inject] private readonly IFileOperationService _fileOperationService;

    /// <summary>
    /// 写入任务文件
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="metadata">任务元数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task WriteAsync(
        string filePath,
        FileTaskMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !_fileOperationService.DirectoryExists(directory))
        {
            _fileOperationService.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(metadata, SchedulingIndentedJsonContext.Default.FileTaskMetadata);
        await _fileOperationService.WriteFileAsync(filePath, json, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 原子写入任务文件（先写临时文件再重命名）
    /// </summary>
    public async Task WriteAtomicAsync(
        string filePath,
        FileTaskMetadata metadata,
        CancellationToken cancellationToken = default)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !_fileOperationService.DirectoryExists(directory))
        {
            _fileOperationService.CreateDirectory(directory);
        }

        var tempPath = filePath + ".tmp";
        var json = JsonSerializer.Serialize(metadata, SchedulingIndentedJsonContext.Default.FileTaskMetadata);

        try
        {
            await _fileOperationService.WriteFileAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
            await _fileOperationService.MoveFileAsync(tempPath, filePath, overwrite: true, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // 清理临时文件
            if (_fileOperationService.FileExists(tempPath))
            {
                try { await _fileOperationService.DeleteFileAsync(tempPath, cancellationToken).ConfigureAwait(false); } catch (Exception ex) {
                    System.Diagnostics.Trace.WriteLine(L.T(StringKey.DeleteTempFileFailedLog, ex.Message));
                }
            }
            throw;
        }
    }

    /// <summary>
    /// 删除任务文件
    /// </summary>
    public async Task<bool> DeleteAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await _fileOperationService.DeleteFileAsync(filePath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 序列化任务为JSON
    /// </summary>
    public static string Serialize(FileTaskMetadata metadata)
    {
        return JsonSerializer.Serialize(metadata, SchedulingIndentedJsonContext.Default.FileTaskMetadata);
    }

    /// <summary>
    /// 反序列化为任务
    /// </summary>
    public static FileTaskMetadata? Deserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, SchedulingJsonContext.Default.FileTaskMetadata);
        }
        catch
        {
            return null;
        }
    }
}
