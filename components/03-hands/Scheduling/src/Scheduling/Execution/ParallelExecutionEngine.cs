
namespace Core.Scheduling;

/// <summary>
/// 并行执行引擎 - 负责实际调度和执行任务
/// </summary>
[Register]
public sealed partial class ParallelExecutionEngine : IAsyncDisposable
{
    private readonly ToolPortingScheduler _scheduler;
    private readonly ISubAgentCoordinator? _agentCoordinator;
    [Inject] private readonly ILogger<ParallelExecutionEngine>? _logger;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly CancellationTokenSource _cts;
    private readonly ConcurrentDictionary<string, AgentExecutionRecord> _agentExecutionRecords = new();
    private bool _disposed;

    /// <summary>
    /// 创建并行执行引擎实例（使用 AgentCoordinator 执行实际任务）
    /// </summary>
    public ParallelExecutionEngine(ISubAgentCoordinator agentCoordinator,  ILogger<ParallelExecutionEngine>? logger = null, ISubAgentContextAccessor? subAgentContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(agentCoordinator);
        _scheduler = new ToolPortingScheduler();
        _agentCoordinator = agentCoordinator;
        _logger = logger;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _cts = new CancellationTokenSource();

        _scheduler.OnDependencyMet += OnDependencyMet;
    }

    /// <summary>
    /// 创建并行执行引擎实例（模拟模式，用于测试）
    /// </summary>
    public ParallelExecutionEngine(bool simulationMode,  ILogger<ParallelExecutionEngine>? logger = null, ISubAgentContextAccessor? subAgentContextAccessor = null)
    {
        if (!simulationMode)
        {
            throw new ArgumentException("此构造函数仅用于模拟模式", nameof(simulationMode));
        }
        _scheduler = new ToolPortingScheduler();
        _agentCoordinator = null;
        _logger = logger;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _cts = new CancellationTokenSource();

        _scheduler.OnDependencyMet += OnDependencyMet;
    }

    /// <summary>
    /// 执行并行计划
    /// </summary>
    public async Task<ExecutionResult> ExecuteAsync(ExecutionOptions? options = null)
    {
        options ??= new ExecutionOptions();
        _logger?.LogInformation("开始执行并行工具移植计划");

        _scheduler.InitializeTasks();

        var allTasks = _scheduler.GetAllTasks();
        _logger?.LogInformation($"总任务数量: {allTasks.Count}");

        var context = new ExecutionContext(options, _cts.Token);
        var executor = new TaskExecutor(_agentCoordinator, _scheduler, _logger, _cts, _agentExecutionRecords, _subAgentContextAccessor);

        try
        {
            await StartInitialTasksAsync(context, executor).ConfigureAwait(false);
            await WaitForCompletionAsync(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger?.LogWarning("执行被取消");
        }

        var report = GenerateExecutionReport();
        _logger?.LogInformation($"并行计划执行完成。完成率: {report.CompletionPercentage:F1}%");

        return new ExecutionResult
        {
            Success = report.FailedTasks.Count == 0,
            Report = report
        };
    }

    /// <summary>
    /// 启动初始可执行任务
    /// </summary>
    private async Task StartInitialTasksAsync(ExecutionContext context, TaskExecutor executor)
    {
        var executableTasks = _scheduler.GetExecutableTasks();
        await StartTasksAsync(executableTasks, context, executor).ConfigureAwait(false);
    }

    /// <summary>
    /// 启动任务集合
    /// </summary>
    private async Task StartTasksAsync(IEnumerable<ScheduledTask> tasks, ExecutionContext context, TaskExecutor executor)
    {
        var taskList = tasks.ToList();
        var newTasks = taskList
            .Where(t => context.TryMarkCompleted(t.Id))
            .Select(async task =>
            {
                var taskExecution = executor.ExecuteWithSemaphoreAsync(task, context);
                await context.AddRunningTaskAsync(taskExecution).ConfigureAwait(false);
                await taskExecution.ConfigureAwait(false);
            });

        await Task.WhenAll(newTasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 等待所有任务完成
    /// </summary>
    private async Task WaitForCompletionAsync(ExecutionContext context)
    {
        while (!context.CancellationToken.IsCancellationRequested)
        {
            var runningTasks = await context.GetRunningTasksSnapshotAsync().ConfigureAwait(false);
            if (runningTasks.Count == 0)
                break;

            // 创建取消任务用于协作式取消
            var cancellationTask = new TaskCompletionSource<object?>();
            using (context.CancellationToken.Register(() => cancellationTask.TrySetResult(null)))
            {
                // 使用 WhenAny 等待任一任务完成或取消令牌触发
                var completedTask = await Task.WhenAny(
                    Task.WhenAny(runningTasks),
                    cancellationTask.Task).ConfigureAwait(false);

                // 如果是取消令牌触发的，则退出
                if (completedTask == cancellationTask.Task)
                {
                    _logger?.LogWarning("执行被取消");
                    break;
                }
            }

            await context.CleanupCompletedTasksAsync().ConfigureAwait(false);

            var executor = new TaskExecutor(_agentCoordinator, _scheduler, _logger, _cts, _agentExecutionRecords, _subAgentContextAccessor);
            var newExecutableTasks = _scheduler.GetExecutableTasks()
                .Where(t => !context.IsCompleted(t.Id));

            await StartTasksAsync(newExecutableTasks, context, executor).ConfigureAwait(false);

            var runningCount = await context.GetRunningTaskCountAsync().ConfigureAwait(false);
            if (IsAllTasksCompleted() && runningCount == 0)
            {
                break;
            }
        }
    }

    /// <summary>
    /// 检查是否所有任务都已完成
    /// </summary>
    private bool IsAllTasksCompleted() =>
        _scheduler.GetAllTasks().All(t =>
            t.Status == ScheduledTaskStatus.Completed || t.Status == ScheduledTaskStatus.Failed);

    /// <summary>
    /// 依赖满足回调
    /// </summary>
    private void OnDependencyMet(object? sender, DependencyMetEventArgs e)
    {
        _logger?.LogInformation($"依赖满足，任务 {e.Task.Name} 现在可以启动");
    }

    /// <summary>
    /// 生成执行报告
    /// </summary>
    private ExecutionReport GenerateExecutionReport()
    {
        var schedulerReport = _scheduler.GetReport();
        var allTasks = schedulerReport.Tasks;

        return new ExecutionReport
        {
            TotalTasks = schedulerReport.TotalTasks,
            CompletedTasks = allTasks.Where(t => t.Status == ScheduledTaskStatus.Completed).ToList(),
            FailedTasks = allTasks.Where(t => t.Status == ScheduledTaskStatus.Failed).ToList(),
            PendingTasks = allTasks.Where(t => t.Status == ScheduledTaskStatus.Pending).ToList(),
            CompletionPercentage = schedulerReport.CompletionPercentage,
            ExecutionDuration = CalculateExecutionDuration(allTasks),
            TaskDetails = allTasks.Select(t => new TaskExecutionDetail
            {
                TaskId = t.Id,
                TaskName = t.Name,
                Status = t.Status,
                RequiredAgents = t.RequiredAgents,
                StartedAt = t.UpdatedAt,
                CompletedAt = t.CompletedAt,
                Duration = t.CompletedAt.HasValue && t.UpdatedAt.HasValue
                    ? t.CompletedAt.Value - t.UpdatedAt.Value
                    : null
            }).ToList()
        };
    }

    /// <summary>
    /// 计算执行持续时间
    /// </summary>
    private static TimeSpan CalculateExecutionDuration(List<ScheduledTask> tasks)
    {
        var startTimes = tasks.Select(t => t.CreatedAt).ToList();
        var endTimes = tasks
            .Where(t => t.CompletedAt.HasValue)
            .Select(t => t.CompletedAt!.Value)
            .ToList();

        if (startTimes.Count == 0 || endTimes.Count == 0)
        {
            return TimeSpan.Zero;
        }

        return endTimes.Max() - startTimes.Min();
    }

    /// <summary>
    /// 取消执行
    /// </summary>
    public void Cancel()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _cts.Cancel();
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            _cts.Cancel();
            _scheduler.OnDependencyMet -= OnDependencyMet;
        }
        finally
        {
            _cts.Dispose();
            GC.SuppressFinalize(this);
        }

        await ValueTask.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>
/// 执行结果
/// </summary>
public sealed partial class ExecutionResult
{
    public required bool Success { get; init; }
    public required ExecutionReport Report { get; init; }
}

/// <summary>
/// 执行报告
/// </summary>
public sealed partial class ExecutionReport
{
    public int TotalTasks { get; init; }
    public List<ScheduledTask> CompletedTasks { get; init; } = new();
    public List<ScheduledTask> FailedTasks { get; init; } = new();
    public List<ScheduledTask> PendingTasks { get; init; } = new();
    public double CompletionPercentage { get; init; }
    public TimeSpan ExecutionDuration { get; init; }
    public List<TaskExecutionDetail> TaskDetails { get; init; } = new();

    public bool AllTasksCompleted => CompletionPercentage >= 100;
}

/// <summary>
/// 任务执行详情
/// </summary>
public sealed partial class TaskExecutionDetail
{
    public required string TaskId { get; init; }
    public required string TaskName { get; init; }
    public required ScheduledTaskStatus Status { get; init; }
    public required int RequiredAgents { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public TimeSpan? Duration { get; init; }
}
