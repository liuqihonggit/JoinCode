
namespace Core.Scheduling;

/// <summary>
/// 任务条目 — 合并 TaskItem 与 TaskStateMachine 的复合容器，按 taskId 统一索引
/// </summary>
public sealed class TaskEntry
{
    /// <summary>任务项数据</summary>
    public TaskItem Item { get; set; }

    /// <summary>任务状态机（可为 null，表示尚未初始化状态机）</summary>
    public TaskStateMachine? StateMachine { get; set; }

    /// <summary>初始化任务条目</summary>
    /// <param name="item">任务项</param>
    public TaskEntry(TaskItem item) => Item = item;

    /// <summary>返回带有新任务项的副本（保持状态机引用，用于并发原子替换）</summary>
    /// <param name="newItem">新任务项</param>
    /// <returns>包含新任务项和当前状态机的新条目</returns>
    public TaskEntry WithItem(TaskItem newItem) => new(newItem) { StateMachine = StateMachine };
}

/// <summary>
/// 内存任务服务实现（仅用于测试和简单场景）
/// 生产环境应使用 FileBasedTaskService（支持跨进程/多智能体协作）
/// </summary>
[Register(typeof(TaskService), ServiceLifetime.Singleton)]
public sealed partial class TaskService : ServiceEntity, ITaskService, IDisposable
{

    /// <summary>
    /// 初始化内存任务服务实例
    /// </summary>
    /// <param name="telemetryService">遥测服务,用于记录任务操作指标</param>
    public TaskService(ITelemetryService? telemetryService = null)
    {
        _telemetryService = telemetryService;
    }
    private readonly ConcurrentDictionary<string, TaskEntry> _tasks = new();
    private readonly ConcurrentDag<string> _dag = new();
    private readonly ITelemetryService? _telemetryService;
    private int _taskCounter;
    private bool _disposed;

    /// <inheritdoc/>
    public Task<OperationResult<TaskItem?>> CreateTaskAsync(
        string title,
        string? description,
        string? assignee,
        DateTime? dueDate,
        string priority,
        List<string>? tags,
        CancellationToken cancellationToken = default)
    {
        var taskId = $"task-{Interlocked.Increment(ref _taskCounter):D4}";
        var task = new TaskItem
        {
            Id = taskId,
            Title = title,
            Description = description,
            Status = TaskExecutionStatusEnumConstants.Pending,
            Priority = TodoPriorityExtensions.FromValue(priority) ?? TodoPriority.Medium,
            Assignee = assignee,
            DueDate = dueDate,
            Tags = tags ?? new List<string>()
        };

        _tasks[taskId] = new TaskEntry(task);
        RecordTaskMetrics("created", task.Priority);
        return Task.FromResult(OperationResult<TaskItem?>.Ok(task));
    }

    /// <inheritdoc/>
    public Task<TaskListResult> ListTasksAsync(
        string? status,
        string? assignee,
        string? priority,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var query = _tasks.Values.Select(e => e.Item).AsEnumerable();

        if (!string.IsNullOrEmpty(status))
        {
            query = query.Where(t => t.Status.Equals(status, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrEmpty(assignee))
        {
            query = query.Where(t => t.Assignee?.Equals(assignee, StringComparison.OrdinalIgnoreCase) == true);
        }

        if (!string.IsNullOrEmpty(priority))
        {
            var priorityEnum = TodoPriorityExtensions.FromValue(priority);
            if (priorityEnum.HasValue)
            {
                query = query.Where(t => t.Priority == priorityEnum.Value);
            }
        }

        var totalCount = query.Count();
        var tasks = query
            .OrderByDescending(t => t.CreatedAt)
            .Skip(offset)
            .Take(limit)
            .ToList();

        return Task.FromResult(new TaskListResult(true, tasks, totalCount));
    }

    /// <inheritdoc/>
    public Task<OperationResult<TaskItem?>> UpdateTaskAsync(
        UpdateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(request.TaskId, out var entry))
        {
            return Task.FromResult(OperationResult<TaskItem?>.Fail(L.T(StringKey.TaskNotFound, request.TaskId)));
        }

        var task = entry.Item;
        var updatedTask = task with
        {
            Title = request.Title ?? task.Title,
            Description = request.Description ?? task.Description,
            Status = request.Status ?? task.Status,
            Assignee = request.Assignee ?? task.Assignee,
            DueDate = request.DueDate ?? task.DueDate,
            Priority = request.Priority != null ? (TodoPriorityExtensions.FromValue(request.Priority) ?? task.Priority) : task.Priority,
            Tags = request.Tags ?? task.Tags
        };

        _tasks[request.TaskId] = entry.WithItem(updatedTask);
        RecordTaskMetrics("updated", updatedTask.Priority);
        return Task.FromResult(OperationResult<TaskItem?>.Ok(updatedTask));
    }

    /// <inheritdoc/>
    public Task<OperationResult<TaskItem?>> StopTaskAsync(
        string taskId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var entry))
        {
            return Task.FromResult(OperationResult<TaskItem?>.Fail(L.T(StringKey.TaskNotFound, taskId)));
        }

        var task = entry.Item;
        var updatedTask = task with
        {
            Status = TaskExecutionStatusEnumConstants.Stopped
        };

        _tasks[taskId] = entry.WithItem(updatedTask);
        RecordTaskMetrics("stopped", updatedTask.Priority);
        return Task.FromResult(OperationResult<TaskItem?>.Ok(updatedTask));
    }

    /// <inheritdoc/>
    public Task<TaskItem?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.TryGetValue(taskId, out var entry);
        return Task.FromResult(entry?.Item);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<TaskDependency>> GetTaskDependenciesAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var dependencies = _dag.Edges.Values
            .Where(e => e.ToId == taskId)
            .Select(e => new TaskDependency
            {
                TaskId = taskId,
                DependsOnTaskId = e.FromId,
                DependencyType = ParseDependencyType(e.Label)
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<TaskDependency>>(dependencies);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<TaskItem?>> SetTaskDependencyAsync(
        string taskId,
        string dependsOnTaskId,
        TaskDependencyType dependencyType = TaskDependencyType.Blocks,
        CancellationToken cancellationToken = default)
    {
        if (!_tasks.ContainsKey(taskId))
        {
            return OperationResult<TaskItem?>.Fail(L.T(StringKey.TaskNotFound, taskId));
        }

        if (!_tasks.ContainsKey(dependsOnTaskId))
        {
            return OperationResult<TaskItem?>.Fail(L.T(StringKey.DepTaskNotExist, dependsOnTaskId));
        }

        if (await _dag.WouldCreateCycleAsync(dependsOnTaskId, taskId, cancellationToken).ConfigureAwait(false))
        {
            return OperationResult<TaskItem?>.Fail(L.T(StringKey.CircularDependencyRejected));
        }

        if (!_dag.Nodes.ContainsKey(taskId))
            await _dag.AddNodeAsync(new DagNode<string> { Id = taskId, Payload = taskId }, cancellationToken).ConfigureAwait(false);
        if (!_dag.Nodes.ContainsKey(dependsOnTaskId))
            await _dag.AddNodeAsync(new DagNode<string> { Id = dependsOnTaskId, Payload = dependsOnTaskId }, cancellationToken).ConfigureAwait(false);

        if (_dag.TryGetEdge(dependsOnTaskId, taskId, out var existingEdge))
        {
            return OperationResult<TaskItem?>.Fail(L.T(StringKey.DependencyAlreadyExists));
        }

        var edgeResult = await _dag.AddEdgeAsync(
            new DagEdge { FromId = dependsOnTaskId, ToId = taskId, Label = dependencyType.ToValue() },
            cancellationToken).ConfigureAwait(false);
        if (!edgeResult.Success)
        {
            return OperationResult<TaskItem?>.Fail(edgeResult.ErrorMessage ?? "Failed to add dependency");
        }

        if (_tasks.TryGetValue(taskId, out var depEntry) && depEntry.StateMachine is { } stateMachine)
        {
            stateMachine.TryTransitionTo(TaskState.WaitingForDependency);
            UpdateTaskStatusFromStateMachine(taskId, stateMachine);
        }

        _tasks.TryGetValue(taskId, out var resultEntry);
        return OperationResult<TaskItem?>.Ok(resultEntry?.Item);
    }

    /// <inheritdoc/>
    public async Task<OperationResult<TaskItem?>> RemoveTaskDependencyAsync(
        string taskId,
        string dependsOnTaskId,
        CancellationToken cancellationToken = default)
    {
        if (!_dag.TryGetEdge(dependsOnTaskId, taskId, out var edgeToRemove))
        {
            return OperationResult<TaskItem?>.Fail(L.T(StringKey.DepNotExist, dependsOnTaskId));
        }

        var result = await _dag.RemoveEdgeAsync(edgeToRemove.Id, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return OperationResult<TaskItem?>.Fail(result.ErrorMessage ?? "Failed to remove dependency");
        }

        CheckAndUpdateTaskState(taskId);

        _tasks.TryGetValue(taskId, out var entry);
        return OperationResult<TaskItem?>.Ok(entry?.Item);
    }

    /// <inheritdoc/>
    public Task<bool> CanExecuteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var entry))
        {
            return Task.FromResult(false);
        }

        var task = entry.Item;
        if (task.Status != TaskExecutionStatusEnumConstants.Pending && task.Status != TaskExecutionStatusEnumConstants.WaitingForDependency)
        {
            return Task.FromResult(false);
        }

        var blockingDeps = _dag.Edges.Values
            .Where(e => e.ToId == taskId && e.Label == TaskDependencyType.Blocks.ToValue());

        foreach (var dep in blockingDeps)
        {
            if (_tasks.TryGetValue(dep.FromId, out var depEntry))
            {
                if (depEntry.Item.Status != TaskExecutionStatusEnumConstants.Completed)
                {
                    return Task.FromResult(false);
                }
            }
        }

        return Task.FromResult(true);
    }

    private void CheckAndUpdateTaskState(string taskId)
    {
        if (!_tasks.TryGetValue(taskId, out var entry) || entry.StateMachine is not { } stateMachine)
        {
            return;
        }

        var hasBlockingDependencies = _dag.Edges.Values
            .Where(e => e.ToId == taskId && e.Label == TaskDependencyType.Blocks.ToValue())
            .Any(e => _tasks.TryGetValue(e.FromId, out var depEntry) && depEntry.Item.Status != TaskExecutionStatusEnumConstants.Completed);

        if (!hasBlockingDependencies && stateMachine.CurrentState == TaskState.WaitingForDependency)
        {
            stateMachine.TryTransitionTo(TaskState.Pending);
            UpdateTaskStatusFromStateMachine(taskId, stateMachine);
        }
    }

    private void UpdateTaskStatusFromStateMachine(string taskId, TaskStateMachine stateMachine)
    {
        var status = TaskExecutionStatusExtensions.ToValue((JoinCode.Abstractions.State.TaskExecutionStatus)stateMachine.CurrentState) ?? TaskExecutionStatusEnumConstants.Pending;

        if (_tasks.TryGetValue(taskId, out var entry))
        {
            _tasks[taskId] = entry.WithItem(entry.Item with { Status = status });
        }
    }

    /// <inheritdoc/>
    public Task<bool> StopTaskAsync(string taskId, bool force, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var entry))
        {
            return Task.FromResult(false);
        }

        var task = entry.Item;
        if (task.Status != TaskExecutionStatusEnumConstants.Running && task.Status != TaskExecutionStatusEnumConstants.Pending && task.Status != TaskExecutionStatusEnumConstants.WaitingForDependency)
        {
            return Task.FromResult(false);
        }

        var updatedTask = task with
        {
            Status = TaskExecutionStatusEnumConstants.Stopped
        };

        _tasks[taskId] = entry.WithItem(updatedTask);

        if (entry.StateMachine is { } stateMachine)
        {
            stateMachine.TryTransitionTo(TaskState.Stopped);
        }

        return Task.FromResult(true);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<RunningTaskInfo>> GetRunningTasksAsync(CancellationToken cancellationToken = default)
    {
        var runningTasks = _tasks.Values
            .Select(e => e.Item)
            .Where(t => t.Status == TaskExecutionStatusEnumConstants.Running)
            .Select(t => new RunningTaskInfo
            {
                Id = t.Id,
                Description = t.Title,
                Status = t.Status,
                StartedAt = t.CreatedAt
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<RunningTaskInfo>>(runningTasks);
    }

    private void RecordTaskMetrics(string operation, TodoPriority? priority = null)
    {
        var tags = new Dictionary<string, string> { ["operation"] = operation };
        if (priority != null) tags["priority"] = priority.Value.ToValue();
        _telemetryService?.RecordCount("task.operation.count", tags, "count", "Task operation count");
    }

    private static TaskDependencyType ParseDependencyType(string label)
    {
        return TaskDependencyTypeExtensions.FromValue(label) ?? TaskDependencyType.Blocks;
    }

    /// <summary>释放资源时回调，释放内部 DAG。</summary>
    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _dag.Dispose();
            base.Dispose();
    }
}
