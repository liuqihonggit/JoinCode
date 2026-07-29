
namespace Services.Shell;

/// <summary>
/// Shell后台任务服务实现 — 对齐 TS LocalShellTask/spawnShellTask
/// 统一走 ShellCommandContext 路径：后台命令先启动进程再立即转后台，复用溢出文件机制
/// </summary>
[Register]
public sealed partial class ShellBackgroundTaskService : IShellBackgroundTaskService, IAsyncDisposable
{
    [Inject] private readonly ILogger<ShellBackgroundTaskService>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;
    [Inject] private readonly IAgentNotificationQueue? _notificationQueue;
    private readonly ConcurrentDictionary<string, ShellBackgroundTaskEntry> _tasks = new();

    /// <inheritdoc />
    public Task<ShellBackgroundTaskInfo> RegisterContextAsync(
        IShellCommandContext context,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        var entry = new ShellBackgroundTaskEntry
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
                    "Shell后台任务 {TaskId} 完成，状态: {Status}, 退出码: {ExitCode}",
                    entry.TaskId, entry.Status, entry.ExitCode);

                RecordBackgroundTaskMetrics(entry.Status.ToString(), result.ExitCode == 0);

                EnqueueTaskNotification(entry, context);
            }
            catch (AggregateException ae) when (ae.InnerException is OperationCanceledException)
            {
                entry.Status = TaskExecutionStatus.Cancelled;
                entry.CompletedAt = DateTime.UtcNow;
                RecordBackgroundTaskMetrics("cancelled", false);
                _logger?.LogInformation("Shell后台任务被取消: {TaskId}", entry.TaskId);

                EnqueueTaskNotification(entry, context, "killed");
            }
            catch (Exception ex)
            {
                entry.Status = TaskExecutionStatus.Failed;
                entry.ErrorMessage = ex.Message;
                entry.CompletedAt = DateTime.UtcNow;
                RecordBackgroundTaskMetrics("error", false);
                _logger?.LogError(ex, "Shell后台任务执行异常: {TaskId}", entry.TaskId);

                EnqueueTaskNotification(entry, context, "failed");
            }
        }, TaskScheduler.Default);

        _logger?.LogInformation("Shell后台任务已注册: {TaskId}, 命令: {Command}", context.TaskId, context.Command);

        return Task.FromResult(ToInfo(entry));
    }

    /// <inheritdoc />
    public Task<ShellBackgroundTaskInfo?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (_tasks.TryGetValue(taskId, out var entry))
        {
            return Task.FromResult<ShellBackgroundTaskInfo?>(ToInfo(entry));
        }

        return Task.FromResult<ShellBackgroundTaskInfo?>(null);
    }

    /// <inheritdoc />
    public Task<List<ShellBackgroundTaskInfo>> ListTasksAsync(CancellationToken cancellationToken = default)
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

        if (entry.Context is not null && entry.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Running)
        {
            try { entry.Context.Kill(); }
            catch (Exception ex) { _logger?.LogDebug(ex, "杀死后台任务进程失败: {TaskId}", taskId); }
        }

        entry.Status = TaskExecutionStatus.Cancelled;
        entry.CompletedAt = DateTime.UtcNow;

        _logger?.LogInformation("Shell后台任务已取消: {TaskId}", taskId);
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<ShellBackgroundTaskInfo> WaitForTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var entry))
        {
            throw new InvalidOperationException($"Background task not found: {taskId}");
        }

        while (entry.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Running)
        {
            await Task.Delay(100, cancellationToken).ConfigureAwait(false);
        }

        return ToInfo(entry);
    }

    /// <inheritdoc />
    /// <remarks>
    /// 对齐 TS TaskOutputTool: 从 ShellCommandContext.GetCurrentStdout() 获取输出
    /// 支持溢出文件 — 后台任务输出不驻留内存
    /// </remarks>
    public Task<string> GetTaskOutputAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (_tasks.TryGetValue(taskId, out var entry))
        {
            var output = new System.Text.StringBuilder();

            if (entry.Context is not null)
            {
                var stdout = entry.Context.GetCurrentStdout();
                var stderr = entry.Context.GetCurrentStderr();

                if (!string.IsNullOrEmpty(stdout))
                {
                    output.AppendLine(stdout);
                }

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

    public Task<List<ShellBackgroundTaskInfo>> ListTasksForAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var infos = _tasks.Values
            .Where(t => t.AgentId == agentId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(ToInfo)
            .ToList();

        return Task.FromResult(infos);
    }

    public Task<int> CancelTasksForAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var agentTaskIds = _tasks.Values
            .Where(t => t.AgentId == agentId && t.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Running)
            .Select(t => t.TaskId)
            .ToList();

        var cancelledCount = 0;
        foreach (var taskId in agentTaskIds)
        {
            if (CancelTaskAsync(taskId, cancellationToken).GetAwaiter().GetResult())
            {
                cancelledCount++;
            }
        }

        if (cancelledCount > 0)
        {
            _logger?.LogInformation("取消 Agent {AgentId} 的后台任务: {Count} 个", agentId, cancelledCount);
        }

        return Task.FromResult(cancelledCount);
    }

    public Task<int> KillAllRunningAsync(CancellationToken cancellationToken = default)
    {
        var runningTasks = _tasks.Values
            .Where(t => t.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Running)
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
        {
            _logger?.LogInformation("强制杀死全部运行中后台任务: {Count} 个", killedCount);
        }

        return Task.FromResult(killedCount);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var entry in _tasks.Values)
        {
            if (entry.Context is not null && entry.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Running)
            {
                try { entry.Context.Kill(); }
                catch (Exception ex) { _logger?.LogDebug(ex, "DisposeAsync 时终止后台任务进程失败"); }
            }
        }

        _tasks.Clear();
        await ValueTask.CompletedTask.ConfigureAwait(false);
    }

    private void RecordBackgroundTaskMetrics(string status, bool isSuccess)
        => _telemetryService?.RecordCount("shell.background.count", new Dictionary<string, string> { ["status"] = status, ["success"] = isSuccess.ToString() }, description: "Shell background task count");

    private static ShellBackgroundTaskInfo ToInfo(ShellBackgroundTaskEntry entry)
    {
        return new ShellBackgroundTaskInfo
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

    private class ShellBackgroundTaskEntry
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

        public IShellCommandContext? Context { get; set; }
    }

    private void EnqueueTaskNotification(ShellBackgroundTaskEntry entry, IShellCommandContext context, string? forcedStatus = null)
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

        _logger?.LogDebug("Shell后台任务通知已入队: {TaskId}, 状态: {Status}", entry.TaskId, status);
    }
}
