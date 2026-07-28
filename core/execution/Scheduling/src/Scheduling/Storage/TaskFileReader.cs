
namespace Core.Scheduling;

/// <summary>
/// 任务文件读取工具类
/// </summary>
[Register]
public sealed partial class TaskFileReader : ITaskFileReader
{
    [Inject] private readonly IFileOperationService _fileOperationService;

    /// <summary>
    /// 读取单个任务文件
    /// </summary>
    /// <param name="filePath">任务文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务元数据，如果文件不存在或格式错误返回null</returns>
    public async Task<FileTaskMetadata?> ReadAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!result.Success || string.IsNullOrWhiteSpace(result.Content))
            {
                return null;
            }

            return JsonSerializer.Deserialize(result.Content, SchedulingJsonContext.Default.FileTaskMetadata);
        }
        catch (JsonException)
        {
            // JSON格式错误
            return null;
        }
        catch (IOException)
        {
            // IO错误
            return null;
        }
    }

    /// <summary>
    /// 读取目录中的所有任务
    /// </summary>
    /// <param name="directoryPath">任务目录路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>任务元数据列表</returns>
    public async Task<List<FileTaskMetadata>> ReadAllAsync(
        string directoryPath,
        CancellationToken cancellationToken = default)
    {
        var tasks = new List<FileTaskMetadata>();

        if (!_fileOperationService.DirectoryExists(directoryPath))
        {
            return tasks;
        }

        var taskFiles = _fileOperationService.GetFiles(
            directoryPath,
            $"{TaskDirectoryOptions.TaskFilePrefix}*{TaskDirectoryOptions.TaskFileExtension}",
            SearchOption.TopDirectoryOnly);

        var readTasks = taskFiles.Select(async filePath =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return await ReadAsync(filePath, cancellationToken).ConfigureAwait(false);
        });
        var results = await Task.WhenAll(readTasks).ConfigureAwait(false);
        tasks.AddRange(results.Where(t => t != null)!);

        return tasks;
    }

    /// <summary>
    /// 检查任务文件是否存在
    /// </summary>
    public bool Exists(string filePath)
    {
        return _fileOperationService.FileExists(filePath);
    }
}
