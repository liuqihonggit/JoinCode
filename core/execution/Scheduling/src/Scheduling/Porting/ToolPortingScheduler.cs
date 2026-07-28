namespace Core.Scheduling;

/// <summary>
/// 工具移植调度器 - 专门用于管理12个工具移植任务的并行执行
/// </summary>
public sealed partial class ToolPortingScheduler
{
    private readonly ParallelTaskScheduler _scheduler;
    [Inject] private readonly ILogger<ToolPortingScheduler>? _logger;

    public ToolPortingScheduler(ILogger<ToolPortingScheduler>? logger = null)
    {
        _logger = logger;
        _scheduler = new ParallelTaskScheduler();
        _scheduler.TaskStatusChanged += OnTaskStatusChanged;
    }

    /// <summary>
    /// 初始化所有12个工具移植任务
    /// </summary>
    public void InitializeTasks()
    {
        // ========== 第一波：无依赖任务（9个）==========

        // Task 01: Agent 调度核心框架 (2智能体, 高优先级 - 阻塞其他任务)
        var task01 = _scheduler.RegisterTask(
            "Task-01-Agent-Core",
            L.T(StringKey.Task01Desc),
            requiredAgents: 2,
            priority: TodoPriority.Critical,
            dependencies: null);

        // Task 03: MCP 工具互操作体系 (2智能体)
        var task03 = _scheduler.RegisterTask(
            "Task-03-MCP-Interop",
            L.T(StringKey.Task03Desc),
            requiredAgents: 2,
            priority: TodoPriority.High,
            dependencies: null);

        // Task 04: 任务生命周期管理 (1智能体)
        var task04 = _scheduler.RegisterTask(
            "Task-04-Task-Lifecycle",
            L.T(StringKey.Task04Desc),
            requiredAgents: 1,
            priority: TodoPriority.High,
            dependencies: null);

        // Task 05: Web 功能实现 (1智能体)
        var task05 = _scheduler.RegisterTask(
            "Task-05-Web-Features",
            L.T(StringKey.Task05Desc),
            requiredAgents: 1,
            priority: TodoPriority.Medium,
            dependencies: null);

        // Task 07: Shell 安全增强(Bash) (1智能体)
        var task07 = _scheduler.RegisterTask(
            "Task-07-Bash-Security",
            L.T(StringKey.Task07Desc),
            requiredAgents: 1,
            priority: TodoPriority.Medium,
            dependencies: null);

        // Task 08: Shell 安全增强(PowerShell) (1智能体)
        var task08 = _scheduler.RegisterTask(
            "Task-08-PowerShell-Security",
            L.T(StringKey.Task08Desc),
            requiredAgents: 1,
            priority: TodoPriority.Medium,
            dependencies: null);

        // Task 09: LSP 语言服务器支持 (2智能体)
        var task09 = _scheduler.RegisterTask(
            "Task-09-LSP-Support",
            L.T(StringKey.Task09Desc),
            requiredAgents: 2,
            priority: TodoPriority.High,
            dependencies: null);

        // Task 10: 配置与定时任务 (1智能体)
        var task10 = _scheduler.RegisterTask(
            "Task-10-Config-Cron",
            L.T(StringKey.Task10Desc),
            requiredAgents: 1,
            priority: TodoPriority.Medium,
            dependencies: null);

        // Task 11: 文件操作增强 (1智能体)
        var task11 = _scheduler.RegisterTask(
            "Task-11-File-Operations",
            L.T(StringKey.Task11Desc),
            requiredAgents: 1,
            priority: TodoPriority.Low,
            dependencies: null);

        // ========== 第二波：依赖任务（3个）==========

        // Task 02: 用户交互与计划模式 (依赖 Task 01)
        var task02 = _scheduler.RegisterTask(
            "Task-02-User-Interaction",
            L.T(StringKey.Task02Desc),
            requiredAgents: 2,
            priority: TodoPriority.High,
            dependencies: new List<string> { task01.Id });

        // Task 06: 待办与工具搜索 (依赖 Task 04)
        var task06 = _scheduler.RegisterTask(
            "Task-06-Todo-Search",
            L.T(StringKey.Task06Desc),
            requiredAgents: 1,
            priority: TodoPriority.Medium,
            dependencies: new List<string> { task04.Id });

        // Task 12: 技能与消息系统 (依赖 Task 01)
        var task12 = _scheduler.RegisterTask(
            "Task-12-Skill-Messaging",
            L.T(StringKey.Task12Desc),
            requiredAgents: 2,
            priority: TodoPriority.High,
            dependencies: new List<string> { task01.Id });
    }

    /// <summary>
    /// 获取第一波可并行执行的任务
    /// </summary>
    public IReadOnlyList<ScheduledTask> GetFirstWaveTasks()
    {
        return _scheduler.GetFirstWaveTasks();
    }

    /// <summary>
    /// 获取当前可执行的任务（依赖已满足）
    /// </summary>
    public IReadOnlyList<ScheduledTask> GetExecutableTasks()
    {
        return _scheduler.GetExecutableTasks();
    }

    /// <summary>
    /// 启动任务
    /// </summary>
    public bool StartTask(string taskId)
    {
        return _scheduler.UpdateTaskStatus(taskId, ScheduledTaskStatus.InProgress, L.T(StringKey.TaskStarted));
    }

    /// <summary>
    /// 完成任务
    /// </summary>
    public bool CompleteTask(string taskId, string? message = null)
    {
        var result = _scheduler.UpdateTaskStatus(taskId, ScheduledTaskStatus.Completed, message ?? L.T(StringKey.TaskCompletedMsg));

        // 检查是否有依赖此任务的其他任务现在可以启动
        if (result)
        {
            var dependentTasks = _scheduler.GetDependentTasks(taskId);
            foreach (var dependentTask in dependentTasks)
            {
                if (_scheduler.AreDependenciesMet(dependentTask.Id) && dependentTask.Status == ScheduledTaskStatus.Pending)
                {
                    OnDependencyMet?.Invoke(this, new DependencyMetEventArgs(dependentTask, taskId));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 标记任务失败
    /// </summary>
    public bool FailTask(string taskId, string errorMessage)
    {
        return _scheduler.UpdateTaskStatus(taskId, ScheduledTaskStatus.Failed, errorMessage);
    }

    /// <summary>
    /// 获取调度报告
    /// </summary>
    public SchedulerReport GetReport()
    {
        return _scheduler.GetReport();
    }

    /// <summary>
    /// 等待所有任务完成
    /// </summary>
    public Task WaitForAllAsync(CancellationToken cancellationToken = default)
    {
        return _scheduler.WaitForAllAsync(cancellationToken);
    }

    /// <summary>
    /// 依赖满足事件 - 当某个任务的依赖全部满足时触发
    /// </summary>
    public event EventHandler<DependencyMetEventArgs>? OnDependencyMet;

    /// <summary>
    /// 任务状态变更事件
    /// </summary>
    public event EventHandler<TaskStatusChangedEventArgs>? TaskStatusChanged
    {
        add => _scheduler.TaskStatusChanged += value;
        remove => _scheduler.TaskStatusChanged -= value;
    }

    private void OnTaskStatusChanged(object? sender, TaskStatusChangedEventArgs e)
    {
        _logger?.LogInformation("TaskStatusChanged: {TaskId} -> {Status}", e.Task.Id, e.Task.Status);
    }

    /// <summary>
    /// 获取所有任务
    /// </summary>
    public List<ScheduledTask> GetAllTasks()
    {
        return _scheduler.GetAllTasks().ToList();
    }

    /// <summary>
    /// 获取任务详情
    /// </summary>
    public ScheduledTask? GetTask(string taskId)
    {
        return _scheduler.GetAllTasks().FirstOrDefault(t => t.Id == taskId);
    }

    /// <summary>
    /// 获取任务ID映射表
    /// </summary>
    public Dictionary<string, string> GetTaskNameToIdMap()
    {
        return _scheduler.GetAllTasks()
            .ToDictionary(t => t.Name, t => t.Id);
    }
}

/// <summary>
/// 依赖满足事件参数
/// </summary>
public sealed partial class DependencyMetEventArgs : EventArgs
{
    public ScheduledTask Task { get; }
    public string CompletedDependencyId { get; }

    public DependencyMetEventArgs(ScheduledTask task, string completedDependencyId)
    {
        Task = task;
        CompletedDependencyId = completedDependencyId;
    }
}

/// <summary>
/// 任务执行上下文 - 用于智能体执行任务
/// </summary>
public sealed partial class TaskExecutionContext
{
    public string TaskId { get; }
    public string TaskName { get; }
    public int AgentIndex { get; }
    public int TotalAgents { get; }
    public CancellationToken CancellationToken { get; }

    public TaskExecutionContext(
        string taskId,
        string taskName,
        int agentIndex,
        int totalAgents,
        CancellationToken cancellationToken)
    {
        TaskId = taskId;
        TaskName = taskName;
        AgentIndex = agentIndex;
        TotalAgents = totalAgents;
        CancellationToken = cancellationToken;
    }
}
