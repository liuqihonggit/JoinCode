namespace Services.Shell;

/// <summary>
/// 前台任务注册表实现 — 基于 MapRegistry，对齐 TS registerForeground/backgroundAll
/// </summary>
[Register]
public sealed partial class ForegroundTaskRegistry : MapRegistry<string, ISystemActuatorCommandContext>, IForegroundTaskRegistry
{

    public ForegroundTaskRegistry(ILogger<ForegroundTaskRegistry>? logger = null)
    {
        _logger = logger;
    }
    private readonly ILogger<ForegroundTaskRegistry>? _logger;

    /// <inheritdoc />
    public void Register(ISystemActuatorCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        AddOrUpdateCore(context.TaskId, context);
        _logger?.LogInformation("注册前台任务: {TaskId}, 命令: {Command}", context.TaskId, context.Command);
    }

    /// <inheritdoc />
    public new void Unregister(string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        RemoveCore(taskId);
    }

    /// <inheritdoc />
    public IEnumerable<string> BackgroundAll()
    {
        var backgrounded = new List<string>();

        foreach (var kvp in EntriesCore)
        {
            var context = kvp.Value;
            if (context.Status == SystemActuatorCommandStatus.Running)
            {
                var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
                if (context.Background(taskId))
                {
                    backgrounded.Add(kvp.Key);
                    _logger?.LogInformation("Ctrl+B 后台化: {OriginalTaskId} -> {NewTaskId}, 命令: {Command}",
                        kvp.Key, taskId, context.Command);
                }
            }
        }

        foreach (var taskId in backgrounded)
        {
            RemoveCore(taskId);
        }

        return backgrounded;
    }

    /// <inheritdoc />
    public bool HasForegroundTasks => Where(t => t.Status == SystemActuatorCommandStatus.Running).Any();

    /// <inheritdoc />
    public IEnumerable<ISystemActuatorCommandContext> GetForegroundTasks()
        => Where(t => t.Status == SystemActuatorCommandStatus.Running);

    /// <inheritdoc />
    public async Task CompactAllAsync(CancellationToken cancellationToken = default)
    {
        var tasks = GetAll().ToList();
        if (tasks.Count == 0) return;

        _logger?.LogInformation("压缩 {Count} 个 Shell 任务", tasks.Count);

        foreach (var task in tasks)
        {
            try
            {
                await task.CompactAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "压缩 Shell 任务失败: {TaskId}", task.TaskId);
            }
        }

        var completed = tasks.Where(t => t.LifecycleState == SystemActuatorLifecycleState.Completed).ToList();
        foreach (var task in completed)
        {
            RemoveCore(task.TaskId);
        }
    }
}
