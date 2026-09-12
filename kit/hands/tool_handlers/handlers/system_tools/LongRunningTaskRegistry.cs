namespace Tools.Handlers;

/// <summary>
/// 长时间任务注册表 — 跟踪超时续期任务，支持 resume/continue/stop 操作
/// 续期策略: kill+重启（非续等原进程），每次续期是全新执行
/// </summary>
public sealed class LongRunningTaskRegistry
{
    private readonly ConcurrentDictionary<string, LongRunningTask> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ISystemActuatorRegistry _actuatorRegistry;
    private readonly ILogger<LongRunningTaskRegistry>? _logger;
    private int _nextId;

    private const int MaxRetries = 5;

    public LongRunningTaskRegistry(ISystemActuatorRegistry actuatorRegistry, ILogger<LongRunningTaskRegistry>? logger = null)
    {
        _actuatorRegistry = actuatorRegistry ?? throw new ArgumentNullException(nameof(actuatorRegistry));
        _logger = logger;
    }

    /// <summary>
    /// 启动续期任务 — 以指定超时重新执行命令
    /// </summary>
    public async Task<LongRunningTaskResult> StartTaskAsync(
        string command,
        string originalTool,
        string? workingDirectory,
        int timeoutMinutes,
        CancellationToken ct = default)
    {
        var taskId = $"task-{Interlocked.Increment(ref _nextId)}";
        var timeoutMs = timeoutMinutes * 60 * 1000;

        var actuatorKind = originalTool.Equals("PowerShell", StringComparison.OrdinalIgnoreCase)
            ? SystemActuatorKind.PowerShell
            : SystemActuatorKind.Bash;

        var actuator = _actuatorRegistry.Get(actuatorKind);
        var stopwatch = Stopwatch.StartNew();

        var task = new LongRunningTask(taskId, command, originalTool, workingDirectory, timeoutMinutes, stopwatch);
        _tasks[taskId] = task;

        try
        {
            var result = await actuator.ExecuteAsync(command, timeoutMs, workingDirectory, cancellationToken: ct).ConfigureAwait(false);
            stopwatch.Stop();

            _tasks.TryRemove(taskId, out _);

            return new LongRunningTaskResult
            {
                TaskId = taskId,
                State = result.ExitCode == 0 ? LongRunningTaskState.Completed : LongRunningTaskState.Failed,
                Stdout = result.Stdout,
                Stderr = result.Stderr,
                ExitCode = result.ExitCode,
                Elapsed = stopwatch.Elapsed,
                RetryCount = 0,
            };
        }
        catch (TimeoutException)
        {
            stopwatch.Stop();
            task.LastElapsed = stopwatch.Elapsed;

            _logger?.LogWarning("续期任务超时 ({Minutes}min): {Command}", timeoutMinutes, command);

            return new LongRunningTaskResult
            {
                TaskId = taskId,
                State = LongRunningTaskState.TimedOut,
                Stdout = string.Empty,
                Stderr = $"命令在 {timeoutMinutes} 分钟内未完成",
                Elapsed = stopwatch.Elapsed,
                RetryCount = 0,
            };
        }
    }

    /// <summary>
    /// 继续续期任务 — 以指定超时重新执行同一命令
    /// </summary>
    public async Task<LongRunningTaskResult> ContinueTaskAsync(
        string taskId,
        int additionalMinutes,
        CancellationToken ct = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
        {
            return new LongRunningTaskResult
            {
                TaskId = taskId,
                State = LongRunningTaskState.NotFound,
                Stderr = $"任务 {taskId} 不存在或已完成",
            };
        }

        if (task.RetryCount >= MaxRetries)
        {
            _tasks.TryRemove(taskId, out _);
            return new LongRunningTaskResult
            {
                TaskId = taskId,
                State = LongRunningTaskState.MaxRetriesExceeded,
                Stderr = $"任务 {taskId} 已达到最大续期次数 ({MaxRetries})",
                RetryCount = task.RetryCount,
            };
        }

        task.RetryCount++;
        var timeoutMs = additionalMinutes * 60 * 1000;

        var actuatorKind = task.OriginalTool.Equals("PowerShell", StringComparison.OrdinalIgnoreCase)
            ? SystemActuatorKind.PowerShell
            : SystemActuatorKind.Bash;

        var actuator = _actuatorRegistry.Get(actuatorKind);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var result = await actuator.ExecuteAsync(task.Command, timeoutMs, task.WorkingDirectory, cancellationToken: ct).ConfigureAwait(false);
            stopwatch.Stop();

            _tasks.TryRemove(taskId, out _);

            return new LongRunningTaskResult
            {
                TaskId = taskId,
                State = result.ExitCode == 0 ? LongRunningTaskState.Completed : LongRunningTaskState.Failed,
                Stdout = result.Stdout,
                Stderr = result.Stderr,
                ExitCode = result.ExitCode,
                Elapsed = stopwatch.Elapsed,
                RetryCount = task.RetryCount,
            };
        }
        catch (TimeoutException)
        {
            stopwatch.Stop();
            task.LastElapsed = stopwatch.Elapsed;

            _logger?.LogWarning("续期任务再次超时 ({Minutes}min, retry {Retry}): {Command}", additionalMinutes, task.RetryCount, task.Command);

            return new LongRunningTaskResult
            {
                TaskId = taskId,
                State = LongRunningTaskState.TimedOut,
                Stdout = string.Empty,
                Stderr = $"命令在 {additionalMinutes} 分钟内未完成（第 {task.RetryCount} 次续期）",
                Elapsed = stopwatch.Elapsed,
                RetryCount = task.RetryCount,
            };
        }
    }

    /// <summary>
    /// 停止续期任务 — 从注册表移除
    /// </summary>
    public bool StopTask(string taskId)
    {
        return _tasks.TryRemove(taskId, out _);
    }

    /// <summary>
    /// 获取任务信息
    /// </summary>
    internal LongRunningTask? GetTask(string taskId)
    {
        return _tasks.TryGetValue(taskId, out var task) ? task : null;
    }
}

/// <summary>长时间任务状态</summary>
public enum LongRunningTaskState
{
    Running,
    Completed,
    Failed,
    TimedOut,
    Stopped,
    NotFound,
    MaxRetriesExceeded,
}

/// <summary>长时间任务结果</summary>
public sealed record LongRunningTaskResult
{
    public required string TaskId { get; init; }
    public required LongRunningTaskState State { get; init; }
    public string Stdout { get; init; } = string.Empty;
    public string Stderr { get; init; } = string.Empty;
    public int? ExitCode { get; init; }
    public TimeSpan Elapsed { get; init; }
    public int RetryCount { get; init; }
}

/// <summary>长时间任务内部记录</summary>
internal sealed class LongRunningTask
{
    public string TaskId { get; }
    public string Command { get; }
    public string OriginalTool { get; }
    public string? WorkingDirectory { get; }
    public int TimeoutMinutes { get; }
    public Stopwatch Stopwatch { get; }
    public int RetryCount { get; set; }
    public TimeSpan LastElapsed { get; set; }

    public LongRunningTask(string taskId, string command, string originalTool, string? workingDirectory, int timeoutMinutes, Stopwatch stopwatch)
    {
        TaskId = taskId;
        Command = command;
        OriginalTool = originalTool;
        WorkingDirectory = workingDirectory;
        TimeoutMinutes = timeoutMinutes;
        Stopwatch = stopwatch;
    }
}
