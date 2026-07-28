
namespace Core.Scheduling;

/// <summary>
/// 并行任务调度器 - 用于协调多智能体并行执行任务
/// </summary>
public sealed class ParallelTaskScheduler
{
    private readonly IClockService _clock;
    private readonly ConcurrentDictionary<string, ScheduledTask> _scheduledTasks = new();
    private readonly ConcurrentDictionary<string, List<string>> _taskDependencies = new();
    private readonly ConcurrentDictionary<string, List<string>> _reverseDependencies = new();
    private readonly ConcurrentBag<TaskCompletionEvent> _completionEvents = new();
    private int _taskCounter;

    /// <summary>
    /// 任务状态变更事件
    /// </summary>
    public event EventHandler<TaskStatusChangedEventArgs>? TaskStatusChanged;

    public ParallelTaskScheduler(IClockService? clock = null)
    {
        _clock = clock ?? SystemClockService.Instance;
    }

    public ScheduledTask RegisterTask(
        string taskName,
        string description,
        int requiredAgents,
        TodoPriority priority,
        List<string>? dependencies = null)
    {
        var taskId = $"scheduled-task-{Interlocked.Increment(ref _taskCounter):D3}";
        var task = new ScheduledTask
        {
            Id = taskId,
            Name = taskName,
            Description = description,
            RequiredAgents = requiredAgents,
            Priority = priority,
            Status = ScheduledTaskStatus.Pending,
            Dependencies = dependencies ?? new List<string>(),
            CreatedAt = _clock.GetUtcNow()
        };

        _scheduledTasks[taskId] = task;

        if (dependencies?.Count > 0)
        {
            _taskDependencies[taskId] = new List<string>(dependencies);
            foreach (var dep in dependencies)
            {
                _reverseDependencies.AddOrUpdate(
                    dep,
                    new List<string> { taskId },
                    (_, list) => { list.Add(taskId); return list; });
            }
        }

        TaskStatusChanged?.Invoke(this, new TaskStatusChangedEventArgs(task, ScheduledTaskStatus.Pending));
        return task;
    }

    /// <summary>
    /// 获取所有已注册的任务
    /// </summary>
    public IReadOnlyCollection<ScheduledTask> GetAllTasks()
    {
        return _scheduledTasks.Values.ToList();
    }

    /// <summary>
    /// 获取指定状态的任务
    /// </summary>
    public IReadOnlyList<ScheduledTask> GetTasksByStatus(ScheduledTaskStatus status)
    {
        return _scheduledTasks.Values.Where(t => t.Status == status).ToList();
    }

    /// <summary>
    /// 获取可执行的任务（依赖已满足且状态为Pending）
    /// </summary>
    public IReadOnlyList<ScheduledTask> GetExecutableTasks()
    {
        return _scheduledTasks.Values
            .Where(t => t.Status == ScheduledTaskStatus.Pending && AreDependenciesMet(t.Id))
            .OrderByDescending(t => t.Priority)
            .ToList();
    }

    /// <summary>
    /// 获取第一波可并行执行的任务（无依赖）
    /// </summary>
    public IReadOnlyList<ScheduledTask> GetFirstWaveTasks()
    {
        return _scheduledTasks.Values
            .Where(t => t.Status == ScheduledTaskStatus.Pending && !t.Dependencies.Any())
            .OrderByDescending(t => t.Priority)
            .ToList();
    }

    /// <summary>
    /// 更新任务状态
    /// </summary>
    public bool UpdateTaskStatus(string taskId, ScheduledTaskStatus newStatus, string? message = null)
    {
        if (!_scheduledTasks.TryGetValue(taskId, out var task))
        {
            return false;
        }

        var oldStatus = task.Status;
        var updatedTask = task with { Status = newStatus, UpdatedAt = _clock.GetUtcNow() };

        if (newStatus == ScheduledTaskStatus.Completed)
        {
            updatedTask = updatedTask with { CompletedAt = _clock.GetUtcNow() };
            _completionEvents.Add(new TaskCompletionEvent(taskId, _clock.GetUtcNow()));
        }

        _scheduledTasks[taskId] = updatedTask;
        TaskStatusChanged?.Invoke(this, new TaskStatusChangedEventArgs(updatedTask, oldStatus, message));

        return true;
    }

    /// <summary>
    /// 检查任务依赖是否已满足
    /// </summary>
    public bool AreDependenciesMet(string taskId)
    {
        if (!_taskDependencies.TryGetValue(taskId, out var dependencies))
        {
            return true;
        }

        return dependencies.All(depId =>
        {
            if (!_scheduledTasks.TryGetValue(depId, out var depTask))
            {
                return false;
            }
            return depTask.Status == ScheduledTaskStatus.Completed;
        });
    }

    /// <summary>
    /// 获取依赖于指定任务的其他任务
    /// </summary>
    public IReadOnlyList<ScheduledTask> GetDependentTasks(string taskId)
    {
        if (!_reverseDependencies.TryGetValue(taskId, out var dependentIds))
        {
            return Array.Empty<ScheduledTask>();
        }

        return dependentIds
            .Select(id => _scheduledTasks.TryGetValue(id, out var task) ? task : null)
            .Where(t => t != null)
            .Cast<ScheduledTask>()
            .ToList();
    }

    /// <summary>
    /// 获取调度状态报告
    /// </summary>
    public SchedulerReport GetReport()
    {
        var tasks = _scheduledTasks.Values.ToList();
        return new SchedulerReport
        {
            TotalTasks = tasks.Count,
            PendingCount = tasks.Count(t => t.Status == ScheduledTaskStatus.Pending),
            InProgressCount = tasks.Count(t => t.Status == ScheduledTaskStatus.InProgress),
            CompletedCount = tasks.Count(t => t.Status == ScheduledTaskStatus.Completed),
            FailedCount = tasks.Count(t => t.Status == ScheduledTaskStatus.Failed),
            Tasks = tasks
        };
    }

    /// <summary>
    /// 等待任务完成
    /// </summary>
    public async Task WaitForTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (_scheduledTasks.TryGetValue(taskId, out var task))
            {
                if (task.Status == ScheduledTaskStatus.Completed || task.Status == ScheduledTaskStatus.Failed)
                {
                    return;
                }
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 等待所有任务完成
    /// </summary>
    public async Task WaitForAllAsync(CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var allCompleted = _scheduledTasks.Values.All(t =>
                t.Status == ScheduledTaskStatus.Completed || t.Status == ScheduledTaskStatus.Failed);

            if (allCompleted)
            {
                return;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// 已调度任务
/// </summary>
public sealed record ScheduledTask
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required int RequiredAgents { get; init; }
    public required TodoPriority Priority { get; init; }
    public ScheduledTaskStatus Status { get; init; } = ScheduledTaskStatus.Pending;
    public List<string> Dependencies { get; init; } = new();
    public DateTime CreatedAt { get; init; }
    public DateTime? UpdatedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public string? LastMessage { get; init; }
}

/// <summary>
/// 调度任务状态 — 仅用于 ParallelTaskScheduler 内部
/// </summary>
public enum ScheduledTaskStatus
{
    [EnumValue("pending")] Pending,
    [EnumValue("inProgress")] InProgress,
    [EnumValue("completed")] Completed,
    [EnumValue("failed")] Failed,
    [EnumValue("cancelled")] Cancelled
}

/// <summary>
/// 任务状态变更事件参数
/// </summary>
public sealed class TaskStatusChangedEventArgs : EventArgs
{
    public ScheduledTask Task { get; }
    public ScheduledTaskStatus OldStatus { get; }
    public string? Message { get; }

    public TaskStatusChangedEventArgs(ScheduledTask task, ScheduledTaskStatus oldStatus, string? message = null)
    {
        Task = task;
        OldStatus = oldStatus;
        Message = message;
    }
}

/// <summary>
/// 任务完成事件
/// </summary>
public sealed record TaskCompletionEvent(string TaskId, DateTime CompletedAt);

/// <summary>
/// 调度器报告
/// </summary>
public sealed record SchedulerReport
{
    public int TotalTasks { get; init; }
    public int PendingCount { get; init; }
    public int InProgressCount { get; init; }
    public int CompletedCount { get; init; }
    public int FailedCount { get; init; }
    public List<ScheduledTask> Tasks { get; init; } = new();

    public bool IsComplete => PendingCount == 0 && InProgressCount == 0;
    public double CompletionPercentage => TotalTasks > 0
        ? (double)CompletedCount / TotalTasks * 100
        : 0;
}
