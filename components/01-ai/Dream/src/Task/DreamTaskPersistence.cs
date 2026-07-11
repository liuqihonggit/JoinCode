
namespace JoinCode.Dream.Persistence;

/// <summary>
/// 做梦任务持久化接口
/// </summary>
public interface IDreamTaskPersistence
{
    /// <summary>
    /// 保存任务状态
    /// </summary>
    Task SaveAsync(DreamTaskState task, CancellationToken ct = default);

    /// <summary>
    /// 加载任务状态
    /// </summary>
    Task<DreamTaskState?> LoadAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// 加载所有任务
    /// </summary>
    Task<IReadOnlyList<DreamTaskState>> LoadAllAsync(CancellationToken ct = default);

    /// <summary>
    /// 删除任务
    /// </summary>
    Task DeleteAsync(string taskId, CancellationToken ct = default);

    /// <summary>
    /// 清理已完成的任务（保留最近N个）
    /// </summary>
    Task CleanupCompletedAsync(int keepCount, CancellationToken ct = default);
}

/// <summary>
/// JSON文件持久化实现
/// </summary>
[Register]
public sealed partial class JsonFileDreamTaskPersistence : IDreamTaskPersistence, IAsyncDisposable
{
    private readonly string _storageDir;
    [Inject] private readonly ILogger<JsonFileDreamTaskPersistence>? _logger;
    private readonly IFileOperationService _fileOperationService;

    public JsonFileDreamTaskPersistence(
        AutoDreamConfig config,
        IFileOperationService fileOperationService,
        
        ILogger<JsonFileDreamTaskPersistence>? logger = null)
    {
        var storageDir = Path.Combine(
            config?.AutoMemoryPath ?? WorkflowConstants.Paths.JccDirectory,
            "tasks");
        _storageDir = storageDir ?? throw new ArgumentNullException(nameof(storageDir));
        _fileOperationService = fileOperationService ?? throw new ArgumentNullException(nameof(fileOperationService));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SaveAsync(DreamTaskState task, CancellationToken ct = default)
    {
        var filePath = GetFilePath(task.Id);
        var dto = DreamTaskDto.FromState(task);

        await using var fileLock = await FileLock.AcquireAsync(filePath, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        var json = JsonSerializer.Serialize(dto, DreamJsonContext.Default.DreamTaskDto);
        var result = await _fileOperationService.WriteFileAsync(filePath, json, ct).ConfigureAwait(false);
        if (!result.Success)
        {
            _logger?.LogError("[DreamTaskPersistence] 保存任务 {TaskId} 失败: {Error}", task.Id, result.ErrorMessage);
        }

        _logger?.LogDebug("[DreamTaskPersistence] 保存任务 {TaskId}", task.Id);
    }

    /// <inheritdoc />
    public async Task<DreamTaskState?> LoadAsync(string taskId, CancellationToken ct = default)
    {
        var filePath = GetFilePath(taskId);

        try
        {
            await using var fileLock = await FileLock.AcquireAsync(filePath, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
            var result = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: ct).ConfigureAwait(false);
            if (!result.Success)
            {
                return null;
            }

            var dto = JsonSerializer.Deserialize(result.Content, DreamJsonContext.Default.DreamTaskDto);
            return dto?.ToState();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DreamTaskPersistence] 加载任务 {TaskId} 失败", taskId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DreamTaskState>> LoadAllAsync(CancellationToken ct = default)
    {
        var tasks = new List<DreamTaskState>();

        try
        {
            if (!_fileOperationService.DirectoryExists(_storageDir))
            {
                return tasks;
            }

            var files = _fileOperationService.GetFiles(_storageDir, "*.json", SearchOption.TopDirectoryOnly);

            foreach (var file in files)
            {
                try
                {
                    var taskId = Path.GetFileNameWithoutExtension(file);
                    var task = await LoadAsync(taskId, ct).ConfigureAwait(false);
                    if (task != null)
                    {
                        tasks.Add(task);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "[DreamTaskPersistence] 加载任务文件失败: {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[DreamTaskPersistence] 加载所有任务失败");
        }

        return tasks.OrderByDescending(t => t.StartTime).ToList();
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string taskId, CancellationToken ct = default)
    {
        var filePath = GetFilePath(taskId);

        await using var fileLock = await FileLock.AcquireAsync(filePath, TimeSpan.FromSeconds(30), ct).ConfigureAwait(false);
        await _fileOperationService.DeleteFileAsync(filePath, ct).ConfigureAwait(false);

        _logger?.LogDebug("[DreamTaskPersistence] 删除任务 {TaskId}", taskId);
    }

    /// <inheritdoc />
    public async Task CleanupCompletedAsync(int keepCount, CancellationToken ct = default)
    {
        var allTasks = await LoadAllAsync(ct).ConfigureAwait(false);

        var completedTasks = allTasks
            .Where(t => t.IsTerminal)
            .OrderByDescending(t => t.EndTime)
            .Skip(keepCount)
            .ToList();

        var deleteTasks = completedTasks.Select(task => DeleteAsync(task.Id, ct));
        await Task.WhenAll(deleteTasks).ConfigureAwait(false);

        if (completedTasks.Count > 0)
        {
            _logger?.LogInformation("[DreamTaskPersistence] 清理了 {Count} 个已完成任务", completedTasks.Count);
        }
    }

    private string GetFilePath(string taskId)
    {
        return Path.Combine(_storageDir, $"{taskId}.json");
    }

    public ValueTask DisposeAsync()
    {
        return ValueTask.CompletedTask;
    }
}
