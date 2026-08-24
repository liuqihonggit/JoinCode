namespace Services.SystemActuator;

/// <summary>
/// 系统执行器注册表 — 合并原 ShellCapabilityCache + ShellProviderFactory + ShellBackgroundTaskService
/// 按 Kind 查找执行器 + 统一管理跨执行器的后台任务
/// </summary>
[Register]
public sealed partial class SystemActuatorRegistry : ISystemActuatorRegistry, IAsyncDisposable
{
    private static FrozenDictionary<SystemActuatorKind, Func<RegistryDeps, ISystemActuator>>? _factories;

    private readonly ILogger<SystemActuatorRegistry>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private readonly IAgentNotificationQueue? _notificationQueue;
    private readonly IFileSystem _fs;
    private readonly ISandboxManager? _sandboxManager;
    private readonly IPreventSleepService? _preventSleepService;
    private readonly ShellExecutionConfig? _config;
    private readonly ConcurrentDictionary<string, SystemActuatorBackgroundTaskEntry> _tasks = new();

    public SystemActuatorRegistry(
        IFileSystem fs,
        ILogger<SystemActuatorRegistry>? logger = null,
        ISandboxManager? sandboxManager = null,
        IPreventSleepService? preventSleepService = null,
        ShellExecutionConfig? config = null,
        ITelemetryService? telemetryService = null,
        IAgentNotificationQueue? notificationQueue = null)
    {
        _fs = fs;
        _logger = logger;
        _sandboxManager = sandboxManager;
        _preventSleepService = preventSleepService;
        _config = config;
        _telemetryService = telemetryService;
        _notificationQueue = notificationQueue;
    }

    /// <summary>
    /// 注册执行器工厂 — 应用启动时调用一次
    /// </summary>
    public static void RegisterFactories(
        IReadOnlyDictionary<SystemActuatorKind, Func<RegistryDeps, ISystemActuator>> factories)
    {
        _factories = factories.ToFrozenDictionary();
    }

    /// <inheritdoc />
    public ISystemActuator Get(SystemActuatorKind kind)
    {
        if (_factories is null)
            throw new InvalidOperationException("SystemActuatorRegistry not initialized. Call RegisterFactories() first.");

        if (!_factories.TryGetValue(kind, out var factory))
            throw new InvalidOperationException($"No SystemActuator registered for {kind.Id}");

        var deps = new RegistryDeps(_fs, _logger, _sandboxManager, _preventSleepService, _config);
        return factory(deps);
    }

    /// <inheritdoc />
    public bool TryGet(SystemActuatorKind kind, [NotNullWhen(true)] out ISystemActuator? actuator)
    {
        if (_factories is null || !_factories.TryGetValue(kind, out var factory))
        {
            actuator = null;
            return false;
        }
        var deps = new RegistryDeps(_fs, _logger, _sandboxManager, _preventSleepService, _config);
        actuator = factory(deps);
        return true;
    }

    /// <inheritdoc />
    public IReadOnlyCollection<SystemActuatorKind> RegisteredKinds
        => _factories?.Keys ?? [];

    /// <inheritdoc />
    public IReadOnlyDictionary<SystemActuatorKind, SystemActuatorInfo> GetAllInfos()
    {
        if (_factories is null)
            return FrozenDictionary<SystemActuatorKind, SystemActuatorInfo>.Empty;

        var deps = new RegistryDeps(_fs, _logger, _sandboxManager, _preventSleepService, _config);
        return _factories.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => kvp.Value(deps) is SystemActuatorBase sab
                ? new SystemActuatorInfo
                {
                    Kind = sab.Kind,
                    DisplayName = sab.DisplayName,
                    ShellPath = sab.ShellPath,
                    Version = sab.Version,
                }
                : new SystemActuatorInfo
                {
                    Kind = kvp.Key,
                    DisplayName = kvp.Key.DisplayName,
                    ShellPath = "",
                    Version = "unknown",
                });
    }

    #region 后台任务管理（原 ShellBackgroundTaskService）

    /// <inheritdoc />
    public Task<SystemActuatorBackgroundTaskInfo> RegisterContextAsync(
        ISystemActuatorCommandContext context,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new SystemActuatorBackgroundTaskEntry
        {
            TaskId = context.TaskId,
            Command = context.Command,
            WorkingDirectory = workingDirectory,
            Status = TaskExecutionStatus.Running,
            CreatedAt = DateTime.UtcNow,
            StartedAt = DateTime.UtcNow,
            Context = context,
        };

        _tasks[context.TaskId] = entry;

        _ = context.ResultTask.ContinueWith(t =>
        {
            try
            {
                var result = t.Result;

                entry.ExitCode = result.ExitCode;
                entry.CompletedAt = DateTime.UtcNow;

                if (result.ExitCode == 0)
                {
                    entry.Status = TaskExecutionStatus.Completed;
                }
                else
                {
                    entry.Status = TaskExecutionStatus.Failed;
                    entry.ErrorMessage = result.ExitCode != 0
                        ? $"Process exited with code {result.ExitCode}"
                        : null;
                }

                _logger?.LogInformation(
                    "后台任务 {TaskId} 完成，状态: {Status}, 退出码: {ExitCode}",
                    entry.TaskId, entry.Status, entry.ExitCode);

                RecordBackgroundTaskMetrics(entry.Status.ToString(), result.ExitCode == 0);
                EnqueueTaskNotification(entry, context);
            }
            catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
            {
                entry.Status = TaskExecutionStatus.Cancelled;
                entry.CompletedAt = DateTime.UtcNow;
                RecordBackgroundTaskMetrics("cancelled", false);
                _logger?.LogInformation("后台任务被取消: {TaskId}", entry.TaskId);
                EnqueueTaskNotification(entry, context, "killed");
            }
            catch (Exception ex)
            {
                entry.Status = TaskExecutionStatus.Failed;
                entry.ErrorMessage = ex.Message;
                entry.CompletedAt = DateTime.UtcNow;
                RecordBackgroundTaskMetrics("error", false);
                _logger?.LogError(ex, "后台任务执行异常: {TaskId}", entry.TaskId);
                EnqueueTaskNotification(entry, context, "failed");
            }
        }, TaskScheduler.Default);

        _logger?.LogInformation("后台任务已注册: {TaskId}, 命令: {Command}", context.TaskId, context.Command);

        return Task.FromResult(ToInfo(entry));
    }

    /// <inheritdoc />
    public Task<SystemActuatorBackgroundTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (_tasks.TryGetValue(taskId, out var entry))
            return Task.FromResult<SystemActuatorBackgroundTaskInfo?>(ToInfo(entry));
        return Task.FromResult<SystemActuatorBackgroundTaskInfo?>(null);
    }

    /// <inheritdoc />
    public Task<List<SystemActuatorBackgroundTaskInfo>> ListTasksAsync(CancellationToken cancellationToken = default)
    {
        var infos = _tasks.Values
            .OrderByDescending(t => t.CreatedAt)
            .Select(ToInfo)
            .ToList();
        return Task.FromResult(infos);
    }

    /// <inheritdoc />
    public Task<bool> CancelTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var entry)) return Task.FromResult(false);

        if (entry.Context is not null && BackgroundTaskStateTransitions.CanCancel(entry.Status))
        {
            try { entry.Context.Kill(); }
            catch (Exception ex) { _logger?.LogDebug(ex, "杀死后台任务进程失败: {TaskId}", taskId); }
        }

        entry.Status = TaskExecutionStatus.Cancelled;
        entry.CompletedAt = DateTime.UtcNow;

        _logger?.LogInformation("后台任务已取消: {TaskId}", taskId);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<SystemActuatorBackgroundTaskInfo> WaitForTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var entry))
            throw new InvalidOperationException($"Background task not found: {taskId}");

        while (BackgroundTaskStateTransitions.CanCancel(entry.Status))
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return ToInfo(entry);
    }

    /// <inheritdoc />
    public Task<string> GetTaskOutputAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (_tasks.TryGetValue(taskId, out var entry))
        {
            var output = new StringBuilder();

            if (entry.Context is not null)
            {
                var stdout = entry.Context.GetCurrentStdout();
                var stderr = entry.Context.GetCurrentStderr();

                if (!string.IsNullOrEmpty(stdout))
                    output.AppendLine(stdout);

                if (!string.IsNullOrEmpty(stderr))
                {
                    output.AppendLine("[stderr]");
                    output.AppendLine(stderr);
                }
            }
            else if (!string.IsNullOrEmpty(entry.Stdout))
            {
                output.AppendLine(entry.Stdout);
                if (!string.IsNullOrEmpty(entry.Stderr))
                {
                    output.AppendLine("[stderr]");
                    output.AppendLine(entry.Stderr);
                }
            }

            return Task.FromResult(output.ToString());
        }

        return Task.FromResult(string.Empty);
    }

    /// <inheritdoc />
    public Task<List<SystemActuatorBackgroundTaskInfo>> ListTasksForAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var infos = _tasks.Values
            .Where(t => t.AgentId == agentId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(ToInfo)
            .ToList();

        return Task.FromResult(infos);
    }

    /// <inheritdoc />
    public async Task<int> CancelTasksForAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var agentTaskIds = _tasks.Values
            .Where(t => t.AgentId == agentId && BackgroundTaskStateTransitions.CanCancel(t.Status))
            .Select(t => t.TaskId)
            .ToList();

        var cancelledCount = 0;
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var taskId in agentTaskIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (await CancelTaskAsync(taskId, cancellationToken).ConfigureAwait(false))
                    cancelledCount++;
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "取消 Agent {AgentId} 的后台任务 {TaskId} 失败（继续取消其余任务）", agentId, taskId);
            }
        }

        if (cancelledCount > 0)
            _logger?.LogInformation("取消 Agent {AgentId} 的后台任务: {Count} 个", agentId, cancelledCount);

        return cancelledCount;
    }

    /// <inheritdoc />
    public Task<int> KillAllRunningAsync(CancellationToken cancellationToken = default)
    {
        var runningTasks = _tasks.Values
            .Where(t => BackgroundTaskStateTransitions.CanCancel(t.Status))
            .ToList();

        var killedCount = 0;
        foreach (var entry in runningTasks)
        {
            if (entry.Context is not null)
            {
                try { entry.Context.Kill(); }
                catch (Exception ex) { _logger?.LogDebug(ex, "杀死后台任务进程失败: {TaskId}", entry.TaskId); }
            }

            entry.Status = TaskExecutionStatus.Cancelled;
            entry.CompletedAt = DateTime.UtcNow;
            killedCount++;
        }

        if (killedCount > 0)
            _logger?.LogInformation("强制杀死全部运行中后台任务: {Count} 个", killedCount);

        return Task.FromResult(killedCount);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _tasks.Values)
        {
            if (entry.Context is not null && BackgroundTaskStateTransitions.CanCancel(entry.Status))
            {
                try { entry.Context.Kill(); }
                catch (Exception ex) { _logger?.LogDebug(ex, "DisposeAsync 时终止后台任务进程失败"); }
            }
        }

        _tasks.Clear();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    #endregion

    #region 私有辅助

    private void RecordBackgroundTaskMetrics(string status, bool isSuccess)
        => _telemetryService?.RecordCount("systemactuator.background.count", new Dictionary<string, string> { ["status"] = status, ["success"] = isSuccess.ToString() }, description: "SystemActuator background task count");

    private static SystemActuatorBackgroundTaskInfo ToInfo(SystemActuatorBackgroundTaskEntry entry)
    {
        return new SystemActuatorBackgroundTaskInfo
        {
            TaskId = entry.TaskId,
            Command = entry.Command,
            Status = entry.Status,
            CreatedAt = entry.CreatedAt,
            StartedAt = entry.StartedAt,
            CompletedAt = entry.CompletedAt,
            Stdout = entry.Stdout,
            Stderr = entry.Stderr,
            ExitCode = entry.ExitCode,
            ErrorMessage = entry.ErrorMessage,
            WorkingDirectory = entry.WorkingDirectory,
            AgentId = entry.AgentId
        };
    }

    private void EnqueueTaskNotification(SystemActuatorBackgroundTaskEntry entry, ISystemActuatorCommandContext context, string? forcedStatus = null)
    {
        if (_notificationQueue is null) return;
        if (Interlocked.CompareExchange(ref entry.Notified, 1, 0) != 0) return;

        var status = forcedStatus ?? (entry.ExitCode == 0 ? "completed" : "failed");
        var description = entry.Command.Length > 80 ? string.Concat(entry.Command.AsSpan(0, 77), "...") : entry.Command;
        var summary = status == "killed"
            ? $"Background command \"{description}\" was killed"
            : $"Background command \"{description}\" {status} (exit code {entry.ExitCode})";

        var xml = $"""
            <task-notification>
            <task-id>{entry.TaskId}</task-id>
            <output-file>{context.OutputFilePath ?? ""}</output-file>
            <status>{status}</status>
            <summary>{summary}</summary>
            </task-notification>
            """;

        _notificationQueue.Enqueue(entry.AgentId, xml);

        _logger?.LogDebug("后台任务通知已入队: {TaskId}, 状态: {Status}", entry.TaskId, status);
    }

    private class SystemActuatorBackgroundTaskEntry
    {
        public required string TaskId { get; init; }
        public required string Command { get; init; }
        public string? WorkingDirectory { get; init; }
        public string? AgentId { get; init; }
        public TaskExecutionStatus Status { get; set; }
        public DateTime CreatedAt { get; init; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Stdout { get; set; }
        public string? Stderr { get; set; }
        public int? ExitCode { get; set; }
        public string? ErrorMessage { get; set; }
        public int Notified;

        public ISystemActuatorCommandContext? Context { get; set; }
    }

    #endregion
}

/// <summary>
/// 注册表依赖项 — 传递给工厂方法创建执行器实例
/// </summary>
public sealed record RegistryDeps(
    IFileSystem FileSystem,
    ILogger? Logger,
    ISandboxManager? SandboxManager,
    IPreventSleepService? PreventSleepService,
    ShellExecutionConfig? Config);
