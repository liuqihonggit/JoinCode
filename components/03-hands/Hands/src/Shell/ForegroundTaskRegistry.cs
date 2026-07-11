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
}
