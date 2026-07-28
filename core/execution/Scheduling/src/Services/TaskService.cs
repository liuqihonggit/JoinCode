
namespace Core.Scheduling;

/// <summary>
/// 内存任务服务实现（仅用于测试和简单场景）
/// 生产环境应使用 FileBasedTaskService（支持跨进程/多智能体协作）
/// </summary>
[Register] // 注册为自身类型，不注册为 ITaskService
public sealed partial class TaskService : ITaskService, IDisposable
{
    private readonly ConcurrentDictionary<string, TaskItem> _tasks = new();
    private readonly ConcurrentDictionary<string, TaskStateMachine> _taskStateMachines = new();
    private readonly ConcurrentDag<string> _dag = new();
    [Inject] private readonly ITelemetryService? _telemetryService;
    private int _taskCounter;

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
            Status = TaskStatusConstants.Pending,
            Priority = TodoPriorityExtensions.FromValue(priority) ?? TodoPriority.Medium,
            Assignee = assignee,
            DueDate = dueDate,
            Tags = tags ?? new List<string>()
        };

        _tasks[taskId] = task;
        RecordTaskMetrics("created", task.Priority);
        return Task.FromResult(OperationResult<TaskItem?>.Ok(task));
    }

    public Task<TaskListResult> ListTasksAsync(
        string? status,
        string? assignee,
        string? priority,
        int limit,
        int offset,
        CancellationToken cancellationToken = default)
    {
        var query = _tasks.Values.AsEnumerable();

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

    public Task<OperationResult<TaskItem?>> UpdateTaskAsync(
        UpdateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(request.TaskId, out var task))
        {
            return Task.FromResult(OperationResult<TaskItem?>.Fail(L.T(StringKey.TaskNotFound, request.TaskId)));
        }

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

        _tasks[request.TaskId] = updatedTask;
        RecordTaskMetrics("updated", updatedTask.Priority);
        return Task.FromResult(OperationResult<TaskItem?>.Ok(updatedTask));
    }

    public Task<OperationResult<TaskItem?>> StopTaskAsync(
        string taskId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
        {
            return Task.FromResult(OperationResult<TaskItem?>.Fail(L.T(StringKey.TaskNotFound, taskId)));
        }

        var updatedTask = task with
        {
            Status = TaskStatusConstants.Stopped
        };

        _tasks[taskId] = updatedTask;
        RecordTaskMetrics("stopped", updatedTask.Priority);
        return Task.FromResult(OperationResult<TaskItem?>.Ok(updatedTask));
    }

    public Task<TaskItem?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.TryGetValue(taskId, out var task);
        return Task.FromResult(task);
    }

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

        var existingEdge = _dag.Edges.Values
            .FirstOrDefault(e => e.FromId == dependsOnTaskId && e.ToId == taskId);
        if (existingEdge is not null)
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

        if (_taskStateMachines.TryGetValue(taskId, out var stateMachine))
        {
            stateMachine.TryTransitionTo(TaskState.WaitingForDependencies);
            UpdateTaskStatusFromStateMachine(taskId, stateMachine);
        }

        _tasks.TryGetValue(taskId, out var task);
        return OperationResult<TaskItem?>.Ok(task);
    }

    public async Task<OperationResult<TaskItem?>> RemoveTaskDependencyAsync(
        string taskId,
        string dependsOnTaskId,
        CancellationToken cancellationToken = default)
    {
        var edgeToRemove = _dag.Edges.Values
            .FirstOrDefault(e => e.FromId == dependsOnTaskId && e.ToId == taskId);
        if (edgeToRemove is null)
        {
            return OperationResult<TaskItem?>.Fail(L.T(StringKey.DepNotExist, dependsOnTaskId));
        }

        var result = await _dag.RemoveEdgeAsync(edgeToRemove.Id, cancellationToken).ConfigureAwait(false);
        if (!result.Success)
        {
            return OperationResult<TaskItem?>.Fail(result.ErrorMessage ?? "Failed to remove dependency");
        }

        CheckAndUpdateTaskState(taskId);

        _tasks.TryGetValue(taskId, out var task);
        return OperationResult<TaskItem?>.Ok(task);
    }

    public Task<bool> CanExecuteTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
        {
            return Task.FromResult(false);
        }

        if (task.Status != TaskStatusConstants.Pending && task.Status != TaskStatusConstants.WaitingForDependencies)
        {
            return Task.FromResult(false);
        }

        var blockingDeps = _dag.Edges.Values
            .Where(e => e.ToId == taskId && e.Label == TaskDependencyType.Blocks.ToValue());

        foreach (var dep in blockingDeps)
        {
            if (_tasks.TryGetValue(dep.FromId, out var dependsOnTask))
            {
                if (dependsOnTask.Status != TaskStatusConstants.Completed)
                {
                    return Task.FromResult(false);
                }
            }
        }

        return Task.FromResult(true);
    }

    private void CheckAndUpdateTaskState(string taskId)
    {
        if (!_taskStateMachines.TryGetValue(taskId, out var stateMachine))
        {
            return;
        }

        var hasBlockingDependencies = _dag.Edges.Values
            .Where(e => e.ToId == taskId && e.Label == TaskDependencyType.Blocks.ToValue())
            .Any(e => _tasks.TryGetValue(e.FromId, out var t) && t.Status != TaskStatusConstants.Completed);

        if (!hasBlockingDependencies && stateMachine.CurrentState == TaskState.WaitingForDependencies)
        {
            stateMachine.TryTransitionTo(TaskState.Pending);
            UpdateTaskStatusFromStateMachine(taskId, stateMachine);
        }
    }

    private void UpdateTaskStatusFromStateMachine(string taskId, TaskStateMachine stateMachine)
    {
        var status = TaskStatusExtensions.ToValue((JoinCode.Abstractions.Models.Task.TaskStatus)stateMachine.CurrentState) ?? TaskStatusConstants.Pending;

        if (_tasks.TryGetValue(taskId, out var task))
        {
            _tasks[taskId] = task with { Status = status };
        }
    }

    public Task<bool> StopTaskAsync(string taskId, bool force, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
        {
            return Task.FromResult(false);
        }

        if (task.Status != TaskStatusConstants.InProgress && task.Status != TaskStatusConstants.Pending && task.Status != TaskStatusConstants.WaitingForDependencies)
        {
            return Task.FromResult(false);
        }

        var updatedTask = task with
        {
            Status = TaskStatusConstants.Stopped
        };

        _tasks[taskId] = updatedTask;

        if (_taskStateMachines.TryGetValue(taskId, out var stateMachine))
        {
            stateMachine.TryTransitionTo(TaskState.Stopped);
        }

        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<RunningTaskInfo>> GetRunningTasksAsync(CancellationToken cancellationToken = default)
    {
        var runningTasks = _tasks.Values
            .Where(t => t.Status == TaskStatusConstants.InProgress)
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

    public void Dispose()
    {
        _dag.Dispose();
    }
}
