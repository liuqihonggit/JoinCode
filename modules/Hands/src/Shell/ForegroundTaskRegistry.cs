namespace Services.Shell;

/// <summary>
/// 前台任务注册表实现 — 对齐 TS registerForeground/backgroundAll
/// </summary>
[Register]
public sealed partial class ForegroundTaskRegistry : IForegroundTaskRegistry
{
    private readonly ConcurrentDictionary<string, IShellCommandContext> _tasks = new();
    [Inject] private readonly ILogger<ForegroundTaskRegistry>? _logger;

    /// <inheritdoc />
    public void Register(IShellCommandContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _tasks[context.TaskId] = context;
        _logger?.LogInformation("注册前台任务: {TaskId}, 命令: {Command}", context.TaskId, context.Command);
    }

    /// <inheritdoc />
    public void Unregister(string taskId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskId);
        _tasks.TryRemove(taskId, out _);
    }

    /// <inheritdoc />
    public IReadOnlyList<string> BackgroundAll()
    {
        var backgrounded = new List<string>();

        foreach (var kvp in _tasks)
        {
            var context = kvp.Value;
            if (context.Status == ShellCommandStatus.Running)
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

        // 清理已后台化的任务
        foreach (var taskId in backgrounded)
        {
            _tasks.TryRemove(taskId, out _);
        }

        return backgrounded;
    }

    /// <inheritdoc />
    public bool HasForegroundTasks => _tasks.Values.Any(t => t.Status == ShellCommandStatus.Running);

    /// <inheritdoc />
    public IReadOnlyList<IShellCommandContext> GetForegroundTasks()
    {
        return _tasks.Values
            .Where(t => t.Status == ShellCommandStatus.Running)
            .ToList();
    }

    /// <inheritdoc />
    public async Task CompactAllAsync(CancellationToken cancellationToken = default)
    {
        var tasks = _tasks.Values.ToList();
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

        var completed = tasks.Where(t => t.LifecycleState == ShellLifecycleState.Completed).ToList();
        foreach (var task in completed)
        {
            _tasks.TryRemove(task.TaskId, out _);
        }
    }
}
