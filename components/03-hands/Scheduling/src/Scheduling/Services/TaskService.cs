
namespace Core.Scheduling;

/// <summary>
/// 内存任务服务实现（仅用于测试和简单场景）
/// 生产环境应使用 FileBasedTaskService（支持跨进程/多智能体协作）
/// </summary>
[Register] // 注册为自身类型，不注册为 ITaskService
public sealed partial class TaskService : ITaskService, IDisposable
{
    private readonly ConcurrentDictionary<string, TaskItem> _tasks = new();
    private readonly ConcurrentDictionary<string, List<TaskDependency>> _taskDependencies = new();
    private readonly ConcurrentDictionary<string, TaskStateMachine> _taskStateMachines = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _dependencyLocks = new();
    [Inject] private readonly ITelemetryService? _telemetryService;
    private int _taskCounter;

    public Task<TaskOperationResult> CreateTaskAsync(
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
        return Task.FromResult(new TaskOperationResult(true, task));
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

    public Task<TaskOperationResult> UpdateTaskAsync(
        UpdateTaskRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(request.TaskId, out var task))
        {
            return Task.FromResult(new TaskOperationResult(false, null, L.T(StringKey.TaskNotFound, request.TaskId)));
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
        return Task.FromResult(new TaskOperationResult(true, updatedTask));
    }

    public Task<TaskOperationResult> StopTaskAsync(
        string taskId,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
        {
            return Task.FromResult(new TaskOperationResult(false, null, L.T(StringKey.TaskNotFound, taskId)));
        }

        var updatedTask = task with
        {
            Status = TaskStatusConstants.Stopped
        };

        _tasks[taskId] = updatedTask;
        RecordTaskMetrics("stopped", updatedTask.Priority);
        return Task.FromResult(new TaskOperationResult(true, updatedTask));
    }

    public Task<TaskItem?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        _tasks.TryGetValue(taskId, out var task);
        return Task.FromResult(task);
    }

    public Task<IReadOnlyList<TaskDependency>> GetTaskDependenciesAsync(string taskId, CancellationToken cancellationToken = default)
    {
        var dependencies = _taskDependencies.TryGetValue(taskId, out var deps)
            ? deps
            : new List<TaskDependency>();
        return Task.FromResult<IReadOnlyList<TaskDependency>>(dependencies);
    }

    public async Task<TaskOperationResult> SetTaskDependencyAsync(
        string taskId,
        string dependsOnTaskId,
        TaskDependencyType dependencyType = TaskDependencyType.Blocks,
        CancellationToken cancellationToken = default)
    {
        if (!_tasks.ContainsKey(taskId))
        {
            return new TaskOperationResult(false, null, L.T(StringKey.TaskNotFound, taskId));
        }

        if (!_tasks.ContainsKey(dependsOnTaskId))
        {
            return new TaskOperationResult(false, null, L.T(StringKey.DepTaskNotExist, dependsOnTaskId));
        }

        if (WouldCreateCircularDependency(taskId, dependsOnTaskId))
        {
            return new TaskOperationResult(false, null, L.T(StringKey.CircularDependencyRejected));
        }

        var dependency = new TaskDependency
        {
            TaskId = taskId,
            DependsOnTaskId = dependsOnTaskId,
            DependencyType = dependencyType
        };

        var dependencies = _taskDependencies.GetOrAdd(taskId, _ => new List<TaskDependency>());
        var depLock = _dependencyLocks.GetOrAdd(taskId, _ => new SemaphoreSlim(1, 1));
        await depLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (dependencies.Any(d => d.DependsOnTaskId == dependsOnTaskId))
            {
                return new TaskOperationResult(false, null, L.T(StringKey.DependencyAlreadyExists));
            }

            dependencies.Add(dependency);
        }
        finally
        {
            depLock.Release();
        }

        if (_taskStateMachines.TryGetValue(taskId, out var stateMachine))
        {
            stateMachine.TryTransitionTo(TaskState.WaitingForDependencies);
            UpdateTaskStatusFromStateMachine(taskId, stateMachine);
        }

        _tasks.TryGetValue(taskId, out var task);
        return new TaskOperationResult(true, task);
    }

    public async Task<TaskOperationResult> RemoveTaskDependencyAsync(
        string taskId,
        string dependsOnTaskId,
        CancellationToken cancellationToken = default)
    {
        if (!_taskDependencies.TryGetValue(taskId, out var dependencies))
        {
            return new TaskOperationResult(false, null, L.T(StringKey.TaskNoDependencies, taskId));
        }

        var depLock = _dependencyLocks.GetOrAdd(taskId, _ => new SemaphoreSlim(1, 1));
        await depLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var dependency = dependencies.FirstOrDefault(d => d.DependsOnTaskId == dependsOnTaskId);
            if (dependency == null)
            {
                return new TaskOperationResult(false, null, L.T(StringKey.DepNotExist, dependsOnTaskId));
            }

            dependencies.Remove(dependency);
        }
        finally
        {
            depLock.Release();
        }

        CheckAndUpdateTaskState(taskId);

        _tasks.TryGetValue(taskId, out var task);
        return new TaskOperationResult(true, task);
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

        if (_taskDependencies.TryGetValue(taskId, out var dependencies))
        {
            foreach (var dependency in dependencies.Where(d => d.DependencyType == TaskDependencyType.Blocks))
            {
                if (_tasks.TryGetValue(dependency.DependsOnTaskId, out var dependsOnTask))
                {
                    if (dependsOnTask.Status != TaskStatusConstants.Completed)
                    {
                        return Task.FromResult(false);
                    }
                }
            }
        }

        return Task.FromResult(true);
    }

    private bool WouldCreateCircularDependency(string taskId, string dependsOnTaskId)
    {
        return HasDependencyPath(dependsOnTaskId, taskId, new HashSet<string>());
    }

    private bool HasDependencyPath(string fromTaskId, string toTaskId, HashSet<string> visited)
    {
        if (fromTaskId == toTaskId)
        {
            return true;
        }

        if (!visited.Add(fromTaskId))
        {
            return false;
        }

        if (_taskDependencies.TryGetValue(fromTaskId, out var dependencies))
        {
            foreach (var dependency in dependencies)
            {
                if (HasDependencyPath(dependency.DependsOnTaskId, toTaskId, visited))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private void CheckAndUpdateTaskState(string taskId)
    {
        if (!_taskStateMachines.TryGetValue(taskId, out var stateMachine))
        {
            return;
        }

        var hasBlockingDependencies = false;
        if (_taskDependencies.TryGetValue(taskId, out var dependencies))
        {
            hasBlockingDependencies = dependencies
                .Where(d => d.DependencyType == TaskDependencyType.Blocks)
                .Any(d => _tasks.TryGetValue(d.DependsOnTaskId, out var t) && t.Status != TaskStatusConstants.Completed);
        }

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

    public void Dispose()
    {
        foreach (var kvp in _dependencyLocks)
            kvp.Value.Dispose();
        _dependencyLocks.Clear();
    }
}
