
namespace Core.Scheduling;

/// <summary>
/// 基于文件系统的任务服务实现
/// 参考 Claude Code 的任务管理设计，支持跨进程/多智能体协作
/// </summary>
[Register]
public sealed partial class FileBasedTaskService : ITaskService, IDisposable
{
    private readonly TaskDirectoryOptions _options;
    private readonly HighWaterMarkManager _highWaterMarkManager;
    private readonly ITaskFileWriter _taskFileWriter;
    private readonly ITaskFileReader _taskFileReader;
    [Inject] private readonly ILogger<FileBasedTaskService>? _logger;
    private readonly IFileOperationService _fileOperationService;
    private readonly SemaphoreSlim _initLock;
    private bool _initialized;

    /// <summary>
    /// 基于文件系统的任务服务构造函数
    /// </summary>
    /// <param name="fileOps">聚合的文件操作依赖</param>
    /// <param name="options">任务目录配置选项</param>
    /// <param name="logger">日志记录器</param>
    public FileBasedTaskService(
        TaskFileOperations fileOps,
        TaskDirectoryOptions? options = null,
        ILogger<FileBasedTaskService>? logger = null)
    {
        _options = options ?? new TaskDirectoryOptions();
        _fileOperationService = fileOps.FileOperationService ?? throw new ArgumentNullException(nameof(fileOps), "FileOperationService cannot be null");
        _taskFileWriter = fileOps.TaskFileWriter ?? throw new ArgumentNullException(nameof(fileOps), "TaskFileWriter cannot be null");
        _taskFileReader = fileOps.TaskFileReader ?? throw new ArgumentNullException(nameof(fileOps), "TaskFileReader cannot be null");
        _logger = logger;
        _highWaterMarkManager = new HighWaterMarkManager(fileOps.FileSystem, _options);
        _initLock = new SemaphoreSlim(1, 1);
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized) return;

            if (!_fileOperationService.DirectoryExists(_options.TaskDirectoryPath))
            {
                _fileOperationService.CreateDirectory(_options.TaskDirectoryPath);
                _logger?.LogInformation(L.T(StringKey.CreateTaskDirLog), _options.TaskDirectoryPath);
            }

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<TaskOperationResult> CreateTaskAsync(
        string title,
        string? description,
        string? assignee,
        DateTime? dueDate,
        string priority,
        List<string>? tags,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            // 生成新任务ID - HighWaterMarkManager 内部已处理锁
            var newId = await _highWaterMarkManager.IncrementAndGetAsync(cancellationToken).ConfigureAwait(false);
            var taskId = $"task-{newId:D4}";

            var task = new TaskItem
            {
                Id = taskId,
                Title = title,
                Description = description,
                Status = TaskState.Pending.ToStateString(),
                Priority = TodoPriorityExtensions.FromValue(priority) ?? TodoPriority.Medium,
                Assignee = assignee,
                DueDate = dueDate,
                Tags = tags ?? new List<string>()
            };

            var metadata = FileTaskMetadata.FromTaskItem(task);
            var filePath = _options.GetTaskFilePath(taskId);

            await _taskFileWriter.WriteAtomicAsync(filePath, metadata, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(L.T(StringKey.CreateTaskLog), taskId, title);

            return new TaskOperationResult(true, task);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.CreateTaskFailedLog));
            return new TaskOperationResult(false, null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<TaskListResult> ListTasksAsync(
        string? status,
        string? assignee,
        string? priority,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var allTasks = await _taskFileReader.ReadAllAsync(_options.TaskDirectoryPath, cancellationToken).ConfigureAwait(false);

            var filtered = allTasks
                .Select(t => t.ToTaskItem())
                .AsEnumerable();

            if (!string.IsNullOrEmpty(status))
            {
                filtered = filtered.Where(t => t.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(assignee))
            {
                filtered = filtered.Where(t => t.Assignee?.Equals(assignee, StringComparison.OrdinalIgnoreCase) == true);
            }

            if (!string.IsNullOrEmpty(priority))
            {
                var priorityEnum = TodoPriorityExtensions.FromValue(priority);
                if (priorityEnum.HasValue)
                {
                    filtered = filtered.Where(t => t.Priority == priorityEnum.Value);
                }
            }

            var totalCount = filtered.Count();
            var tasks = filtered
                .OrderByDescending(t => t.CreatedAt)
                .Skip(offset)
                .Take(limit)
                .ToList();

            return new TaskListResult(true, tasks, totalCount);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.ListTaskFailedLog));
            return new TaskListResult(false, new List<TaskItem>(), 0, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<TaskItem?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var filePath = _options.GetTaskFilePath(taskId);
        var metadata = await _taskFileReader.ReadAsync(filePath, cancellationToken).ConfigureAwait(false);

        return metadata?.ToTaskItem();
    }

    /// <inheritdoc />
    public async Task<TaskOperationResult> UpdateTaskAsync(
        UpdateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var filePath = _options.GetTaskFilePath(request.TaskId);

        // 先检查任务是否存在
        var existing = await _taskFileReader.ReadAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (existing == null)
        {
            return new TaskOperationResult(false, null, L.T(StringKey.TaskNotExist, request.TaskId));
        }

        try
        {
            // 读取最新内容
            var readResult = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!readResult.Success)
            {
                return new TaskOperationResult(false, null, L.T(StringKey.TaskNotExist, request.TaskId));
            }

            var latest = FileTaskMetadata.FromJson(readResult.Content);
            if (latest == null)
            {
                return new TaskOperationResult(false, null, L.T(StringKey.TaskNotExist, request.TaskId));
            }

            var updated = latest with
            {
                Title = request.Title ?? latest.Title,
                Description = request.Description ?? latest.Description,
                Status = request.Status ?? latest.Status,
                Assignee = request.Assignee ?? latest.Assignee,
                DueDate = request.DueDate ?? latest.DueDate,
                Priority = request.Priority ?? latest.Priority,
                Tags = request.Tags ?? latest.Tags
            };

            await _fileOperationService.WriteFileAsync(filePath, updated.ToJson(), cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation(L.T(StringKey.UpdateTaskLog), request.TaskId);

            return new TaskOperationResult(true, updated.ToTaskItem());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.UpdateTaskFailedLog), request.TaskId);
            return new TaskOperationResult(false, null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<TaskOperationResult> StopTaskAsync(
        string taskId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        return await UpdateTaskAsync(new UpdateTaskRequest { TaskId = taskId, Status = TaskState.Stopped.ToStateString() }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TaskDependency>> GetTaskDependenciesAsync(
        string taskId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var filePath = _options.GetTaskFilePath(taskId);
        var metadata = await _taskFileReader.ReadAsync(filePath, cancellationToken).ConfigureAwait(false);

        if (metadata == null)
        {
            return new List<TaskDependency>();
        }

        var dependencies = metadata.Dependencies
            .Select(depId => new TaskDependency
            {
                TaskId = taskId,
                DependsOnTaskId = depId,
                DependencyType = TaskDependencyType.Blocks
            })
            .ToList();

        return dependencies;
    }

    /// <inheritdoc />
    public async Task<TaskOperationResult> SetTaskDependencyAsync(
        string taskId,
        string dependsOnTaskId,
        TaskDependencyType dependencyType = TaskDependencyType.Blocks,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var filePath = _options.GetTaskFilePath(taskId);

        var existing = await _taskFileReader.ReadAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (existing == null)
        {
            return new TaskOperationResult(false, null, L.T(StringKey.TaskNotExist, taskId));
        }

        try
        {
            var readResult = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!readResult.Success)
            {
                return new TaskOperationResult(false, null, L.T(StringKey.TaskNotExist, taskId));
            }

            var latest = FileTaskMetadata.FromJson(readResult.Content);
            if (latest == null)
            {
                return new TaskOperationResult(false, null, L.T(StringKey.TaskNotExist, taskId));
            }

            var updated = latest with
            {
                Dependencies = dependencyType == TaskDependencyType.Blocks
                    ? latest.Dependencies.Concat(new[] { dependsOnTaskId }).ToList()
                    : latest.Dependencies,
                BlockedBy = dependencyType == TaskDependencyType.Blocks
                    ? latest.BlockedBy.Concat(new[] { dependsOnTaskId }).ToList()
                    : latest.BlockedBy
            };

            await _fileOperationService.WriteFileAsync(filePath, updated.ToJson(), cancellationToken).ConfigureAwait(false);

            return new TaskOperationResult(true, updated.ToTaskItem());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.SetTaskDepFailedLog), taskId);
            return new TaskOperationResult(false, null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<TaskOperationResult> RemoveTaskDependencyAsync(
        string taskId,
        string dependsOnTaskId,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var filePath = _options.GetTaskFilePath(taskId);

        var existing = await _taskFileReader.ReadAsync(filePath, cancellationToken).ConfigureAwait(false);
        if (existing == null)
        {
            return new TaskOperationResult(false, null, L.T(StringKey.TaskNotExist, taskId));
        }

        try
        {
            var readResult = await _fileOperationService.ReadFileAsync(filePath, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (!readResult.Success)
            {
                return new TaskOperationResult(false, null, L.T(StringKey.TaskNotExist, taskId));
            }

            var latest = FileTaskMetadata.FromJson(readResult.Content);
            if (latest == null)
            {
                return new TaskOperationResult(false, null, L.T(StringKey.TaskNotExist, taskId));
            }

            var updated = latest with
            {
                Dependencies = latest.Dependencies.Where(d => d != dependsOnTaskId).ToList(),
                BlockedBy = latest.BlockedBy.Where(d => d != dependsOnTaskId).ToList()
            };

            await _fileOperationService.WriteFileAsync(filePath, updated.ToJson(), cancellationToken).ConfigureAwait(false);

            return new TaskOperationResult(true, updated.ToTaskItem());
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.RemoveTaskDepFailedLog), taskId);
            return new TaskOperationResult(false, null, ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<bool> CanExecuteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var task = await GetTaskAsync(taskId, cancellationToken).ConfigureAwait(false);
        if (task == null)
        {
            return false;
        }

        var dependencies = await GetTaskDependenciesAsync(taskId, cancellationToken).ConfigureAwait(false);

        foreach (var dep in dependencies.Where(d => d.DependencyType == TaskDependencyType.Blocks))
        {
            var depTask = await GetTaskAsync(dep.DependsOnTaskId, cancellationToken).ConfigureAwait(false);
            if (depTask == null || depTask.Status != TaskState.Completed.ToStateString())
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> StopTaskAsync(
        string taskId,
        bool force,
        CancellationToken cancellationToken = default)
    {
        var result = await UpdateTaskAsync(new UpdateTaskRequest { TaskId = taskId, Status = TaskState.Stopped.ToStateString() }, cancellationToken).ConfigureAwait(false);
        return result.Success;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RunningTaskInfo>> GetRunningTasksAsync(CancellationToken cancellationToken = default)
    {
        var result = await ListTasksAsync(TaskState.InProgress.ToStateString(), null, null, 100, 0, cancellationToken).ConfigureAwait(false);

        return result.Tasks.Select(t => new RunningTaskInfo
        {
            Id = t.Id,
            Description = t.Title,
            Status = t.Status,
            StartedAt = t.CreatedAt
        }).ToList();
    }

    /// <summary>
    /// 重置任务列表（保留高水位标记）
    /// </summary>
    public async Task ResetTaskListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        // 找到最高ID
        var allTasks = await _taskFileReader.ReadAllAsync(_options.TaskDirectoryPath, cancellationToken).ConfigureAwait(false);
        var maxId = allTasks
            .Select(t => ParseTaskId(t.Id))
            .DefaultIfEmpty(0)
            .Max();

        // 更新高水位标记
        var highWaterMarkPath = _options.GetHighWaterMarkPath();
        await _fileOperationService.WriteFileAsync(highWaterMarkPath, maxId.ToString(), cancellationToken).ConfigureAwait(false);

        // 删除所有任务文件
        var taskFiles = _fileOperationService.GetFiles(
            _options.TaskDirectoryPath,
            $"{TaskDirectoryOptions.TaskFilePrefix}*{TaskDirectoryOptions.TaskFileExtension}",
            SearchOption.TopDirectoryOnly);

        var deleteTasks = taskFiles.Select(async file =>
        {
            try
            {
                await _fileOperationService.DeleteFileAsync(file, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, L.T(StringKey.DeleteTaskFileFailedLog), file);
            }
        });
        await Task.WhenAll(deleteTasks).ConfigureAwait(false);

        _logger?.LogInformation(L.T(StringKey.TaskListResetLog), maxId);
    }

    /// <summary>
    /// 删除任务
    /// </summary>
    public async Task<bool> DeleteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        var filePath = _options.GetTaskFilePath(taskId);

        if (!_fileOperationService.FileExists(filePath))
        {
            return false;
        }

        try
        {
            await _fileOperationService.DeleteFileAsync(filePath, cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 从任务ID解析整数值
    /// </summary>
    private static int ParseTaskId(string taskId)
    {
        // 移除 "task-" 前缀 - 使用 Span 避免 Substring 分配
        if (taskId.StartsWith("task-", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(taskId.AsSpan(5), out var parsedValue) ? parsedValue : 0;
        }

        if (int.TryParse(taskId, out var value))
        {
            return value;
        }
        return 0;
    }

    public void Dispose() => _initLock.Dispose();
}
