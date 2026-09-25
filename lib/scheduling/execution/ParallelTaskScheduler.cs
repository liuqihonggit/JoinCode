
namespace Core.Scheduling;

/// <summary>
/// 并行任务调度器 - 用于协调多智能体并行执行任务
/// </summary>
public sealed class ParallelTaskScheduler {
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

    /// <summary>
    /// 初始化并行任务调度器
    /// </summary>
    /// <param name="clock">时钟服务,用于获取当前时间;为空时使用系统默认时钟</param>
    public ParallelTaskScheduler(IClockService? clock = null) {
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <summary>
    /// 注册一个新任务到调度器
    /// </summary>
    /// <param name="taskName">任务名称</param>
    /// <param name="description">任务描述</param>
    /// <param name="requiredAgents">执行该任务所需的智能体数量</param>
    /// <param name="priority">任务优先级</param>
    /// <param name="dependencies">依赖的任务Id列表,为空表示无依赖</param>
    /// <returns>已注册的任务对象</returns>
    public ScheduledTask RegisterTask(
        string taskName,
        string description,
        int requiredAgents,
        TodoPriority priority,
        List<string>? dependencies = null) {
        var taskId = $"scheduled-task-{Interlocked.Increment(ref _taskCounter):D3}";
        var task = new ScheduledTask {
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

        if (dependencies?.Count > 0) {
            _taskDependencies[taskId] = new List<string>(dependencies);
            foreach (var dep in dependencies) {
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
    /// 获取所有已注册任务的快照拷贝
    /// </summary>
    public ScheduledTask[] GetAllTasks() => _scheduledTasks.Values.ToArray();

    /// <summary>
    /// 获取指定状态的任务
    /// </summary>
    public IEnumerable<ScheduledTask> GetTasksByStatus(ScheduledTaskStatus status) {
        return _scheduledTasks.Values.Where(t => t.Status == status);
    }

    /// <summary>
    /// 获取可执行的任务（依赖已满足且状态为Pending）
    /// </summary>
    public IEnumerable<ScheduledTask> GetExecutableTasks() {
        return _scheduledTasks.Values
            .Where(t => t.Status == ScheduledTaskStatus.Pending && AreDependenciesMet(t.Id))
            .OrderByDescending(t => t.Priority);
    }

    /// <summary>
    /// 获取第一波可并行执行的任务（无依赖）
    /// </summary>
    public IEnumerable<ScheduledTask> GetFirstWaveTasks() {
        return _scheduledTasks.Values
            .Where(t => t.Status == ScheduledTaskStatus.Pending && !t.Dependencies.Any())
            .OrderByDescending(t => t.Priority);
    }

    /// <summary>
    /// 更新任务状态
    /// </summary>
    public bool UpdateTaskStatus(string taskId, ScheduledTaskStatus newStatus, string? message = null) {
        if (!_scheduledTasks.TryGetValue(taskId, out var task)) {
            return false;
        }

        var oldStatus = task.Status;
        var updatedTask = task with { Status = newStatus, UpdatedAt = _clock.GetUtcNow() };

        if (newStatus == ScheduledTaskStatus.Completed) {
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
    public bool AreDependenciesMet(string taskId) {
        if (!_taskDependencies.TryGetValue(taskId, out var dependencies)) {
            return true;
        }

        return dependencies.All(depId => {
            if (!_scheduledTasks.TryGetValue(depId, out var depTask)) {
                return false;
            }
            return depTask.Status == ScheduledTaskStatus.Completed;
        });
    }

    /// <summary>
    /// 获取依赖于指定任务的其他任务
    /// </summary>
    public IEnumerable<ScheduledTask> GetDependentTasks(string taskId) {
        if (!_reverseDependencies.TryGetValue(taskId, out var dependentIds)) {
            return [];
        }

        return dependentIds
            .Select(id => _scheduledTasks.TryGetValue(id, out var task) ? task : null)
            .Where(t => t != null)
            .Cast<ScheduledTask>();
    }

    /// <summary>
    /// 获取调度状态报告
    /// </summary>
    public SchedulerReport GetReport() {
        var tasks = _scheduledTasks.Values.ToList();
        return new SchedulerReport {
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
    public async Task WaitForTaskAsync(string taskId, CancellationToken cancellationToken = default) {
        while (!cancellationToken.IsCancellationRequested) {
            if (_scheduledTasks.TryGetValue(taskId, out var task)) {
                if (task.Status == ScheduledTaskStatus.Completed || task.Status == ScheduledTaskStatus.Failed) {
                    return;
                }
            }
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 等待所有任务完成
    /// </summary>
    public async Task WaitForAllAsync(CancellationToken cancellationToken = default) {
        while (!cancellationToken.IsCancellationRequested) {
            var allCompleted = _scheduledTasks.Values.All(t =>
                t.Status == ScheduledTaskStatus.Completed || t.Status == ScheduledTaskStatus.Failed);

            if (allCompleted) {
                return;
            }

            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }
    }
}

/// <summary>
/// 已调度任务
/// </summary>
public sealed record ScheduledTask {
    /// <summary>任务唯一标识</summary>
    public required string Id { get; init; }
    /// <summary>任务名称</summary>
    public required string Name { get; init; }
    /// <summary>任务描述</summary>
    public required string Description { get; init; }
    /// <summary>执行该任务所需的智能体数量</summary>
    public required int RequiredAgents { get; init; }
    /// <summary>任务优先级</summary>
    public required TodoPriority Priority { get; init; }
    /// <summary>任务当前状态,默认为待执行</summary>
    public ScheduledTaskStatus Status { get; init; } = ScheduledTaskStatus.Pending;
    /// <summary>依赖的任务Id列表</summary>
    public List<string> Dependencies { get; init; } = new();
    /// <summary>任务创建时间</summary>
    public DateTime CreatedAt { get; init; }
    /// <summary>任务最后更新时间</summary>
    public DateTime? UpdatedAt { get; init; }
    /// <summary>任务完成时间</summary>
    public DateTime? CompletedAt { get; init; }
    /// <summary>任务最后消息,用于记录状态变更时的附加信息</summary>
    public string? LastMessage { get; init; }
}

/// <summary>
/// 调度任务状态 — 仅用于 ParallelTaskScheduler 内部
/// </summary>
public enum ScheduledTaskStatus {
    /// <summary>待执行</summary>
    [EnumValue("pending")] Pending,
    /// <summary>执行中</summary>
    [EnumValue("inProgress")] InProgress,
    /// <summary>已完成</summary>
    [EnumValue("completed")] Completed,
    /// <summary>执行失败</summary>
    [EnumValue("failed")] Failed,
    /// <summary>已取消</summary>
    [EnumValue("cancelled")] Cancelled
}

/// <summary>
/// 任务状态变更事件参数
/// </summary>
public sealed class TaskStatusChangedEventArgs : EventArgs {
    /// <summary>状态变更关联的任务</summary>
    public ScheduledTask Task { get; }
    /// <summary>变更前的旧状态</summary>
    public ScheduledTaskStatus OldStatus { get; }
    /// <summary>状态变更附加消息</summary>
    public string? Message { get; }

    /// <summary>
    /// 初始化任务状态变更事件参数
    /// </summary>
    /// <param name="task">关联的任务对象</param>
    /// <param name="oldStatus">变更前的旧状态</param>
    /// <param name="message">状态变更附加消息</param>
    public TaskStatusChangedEventArgs(ScheduledTask task, ScheduledTaskStatus oldStatus, string? message = null) {
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
public sealed record SchedulerReport {
    /// <summary>任务总数</summary>
    public int TotalTasks { get; init; }
    /// <summary>待执行任务数</summary>
    public int PendingCount { get; init; }
    /// <summary>执行中任务数</summary>
    public int InProgressCount { get; init; }
    /// <summary>已完成任务数</summary>
    public int CompletedCount { get; init; }
    /// <summary>失败任务数</summary>
    public int FailedCount { get; init; }
    /// <summary>所有任务列表</summary>
    public List<ScheduledTask> Tasks { get; init; } = new();

    /// <summary>是否全部任务已完成(无待执行且无执行中)</summary>
    public bool IsComplete => PendingCount == 0 && InProgressCount == 0;
    /// <summary>完成百分比(0-100)</summary>
    public double CompletionPercentage => TotalTasks > 0
        ? (double)CompletedCount / TotalTasks * 100
        : 0;
}