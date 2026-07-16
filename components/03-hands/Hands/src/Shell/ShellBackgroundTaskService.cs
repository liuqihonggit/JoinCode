
namespace Services.Shell;

/// <summary>
/// Shell后台任务服务实现 - 支持后台执行长时间运行的命令
/// </summary>
[Register]
public sealed partial class ShellBackgroundTaskService : IShellBackgroundTaskService, IDisposable
{
    [Inject] private readonly IShellExecutionService _shellExecutionService;
    [Inject] private readonly ILogger<ShellBackgroundTaskService>? _logger;
    [Inject] private readonly ITelemetryService? _telemetryService;
    private readonly ConcurrentDictionary<string, ShellBackgroundTaskEntry> _tasks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();

    /// <inheritdoc />
    public Task<ShellBackgroundTaskInfo> CreateTaskAsync(
        string command,
        string? workingDirectory = null,
        CancellationToken cancellationToken = default)
    {
        return CreateTaskAsync(command, workingDirectory, agentId: null, cancellationToken);
    }

    public Task<ShellBackgroundTaskInfo> CreateTaskAsync(
        string command,
        string? workingDirectory = null,
        string? agentId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new ArgumentException(L.T(StringKey.ShellBgCommandCannotBeEmpty), nameof(command));
        }

        // 使用现有的TaskIdGenerator生成任务ID
        var taskId = TaskIdGenerator.GenerateTaskId(TaskType.LocalBash);
        var cts = new CancellationTokenSource();
        _cancellationTokens[taskId] = cts;

        var entry = new ShellBackgroundTaskEntry
        {
            TaskId = taskId,
            Command = command,
            WorkingDirectory = workingDirectory,
            AgentId = agentId,
            Status = TaskExecutionStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        _tasks[taskId] = entry;

        _ = Task.Run(async () =>
        {
            try
            {
                await ExecuteTaskAsync(entry, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "后台任务执行异常: {TaskId}", entry.TaskId);
            }
        }, CancellationToken.None);

        _logger?.LogInformation(L.T(StringKey.ShellBgTaskCreated), taskId, command);

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
        if (_cancellationTokens.TryRemove(taskId, out var cts))
        {
            cts.Cancel();
            cts.Dispose();

            if (_tasks.TryGetValue(taskId, out var entry))
            {
                entry.Status = TaskExecutionStatus.Cancelled;
                entry.CompletedAt = DateTime.UtcNow;
            }

            _logger?.LogInformation(L.T(StringKey.ShellBgTaskCancelled), taskId);
            return Task.FromResult(true);
        }

        return Task.FromResult(false);
    }

    /// <inheritdoc />
    public async Task<ShellBackgroundTaskInfo> WaitForTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var entry))
        {
            throw new InvalidOperationException(L.T(StringKey.ShellBgTaskNotExist, taskId));
        }

        // 等待任务完成
        while (entry.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Running)
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
            var output = new System.Text.StringBuilder();

            if (!string.IsNullOrEmpty(entry.Stdout))
            {
                output.AppendLine(L.T(StringKey.ShellBgStdoutLabel));
                output.AppendLine(entry.Stdout);
            }

            if (!string.IsNullOrEmpty(entry.Stderr))
            {
                output.AppendLine(L.T(StringKey.ShellBgStderrLabel));
                output.AppendLine(entry.Stderr);
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
            if (_cancellationTokens.TryRemove(taskId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
                cancelledCount++;
            }

            if (_tasks.TryGetValue(taskId, out var entry))
            {
                entry.Status = TaskExecutionStatus.Cancelled;
                entry.CompletedAt = DateTime.UtcNow;
            }
        }

        if (cancelledCount > 0)
        {
            _logger?.LogInformation(L.T(StringKey.ShellBgCancelAgentTasks), agentId, cancelledCount);
        }

        return Task.FromResult(cancelledCount);
    }

    private async Task ExecuteTaskAsync(ShellBackgroundTaskEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            entry.Status = TaskExecutionStatus.Running;
            entry.StartedAt = DateTime.UtcNow;

            _logger?.LogInformation(L.T(StringKey.ShellBgStartExecution), entry.TaskId);

            var result = await _shellExecutionService.ExecuteAsync(
                entry.Command,
                workingDirectory: entry.WorkingDirectory,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            entry.Stdout = result.Stdout;
            entry.Stderr = result.Stderr;
            entry.ExitCode = result.ExitCode;
            entry.CompletedAt = DateTime.UtcNow;

            if (cancellationToken.IsCancellationRequested)
            {
                entry.Status = TaskExecutionStatus.Cancelled;
            }
            else if (result.Success)
            {
                entry.Status = TaskExecutionStatus.Completed;
            }
            else
            {
                entry.Status = TaskExecutionStatus.Failed;
                entry.ErrorMessage = result.ErrorMessage ?? L.T(StringKey.ShellBgExecutionFailed);
            }

            _logger?.LogInformation(
                "Shell后台任务 {TaskId} 完成，状态: {Status}, 退出码: {ExitCode}",
                entry.TaskId,
                entry.Status,
                entry.ExitCode);

            RecordBackgroundTaskMetrics(entry.Status.ToString(), entry.ExitCode == 0);
        }
        catch (OperationCanceledException)
        {
            entry.Status = TaskExecutionStatus.Cancelled;
            entry.CompletedAt = DateTime.UtcNow;
            RecordBackgroundTaskMetrics("cancelled", false);
            _logger?.LogInformation(L.T(StringKey.ShellBgTaskCancelledByException), entry.TaskId);
        }
        catch (Exception ex)
        {
            entry.Status = TaskExecutionStatus.Failed;
            entry.ErrorMessage = ex.Message;
            entry.CompletedAt = DateTime.UtcNow;
            RecordBackgroundTaskMetrics("error", false);
            _logger?.LogError(ex, L.T(StringKey.ShellBgTaskExecutionFailed), entry.TaskId);
        }
        finally
        {
            _cancellationTokens.TryRemove(entry.TaskId, out _);
        }
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

    public Task<int> KillAllRunningAsync(CancellationToken cancellationToken = default)
    {
        var runningTasks = _tasks.Values
            .Where(t => t.Status is TaskExecutionStatus.Pending or TaskExecutionStatus.Running)
            .ToList();

        var killedCount = 0;
        foreach (var entry in runningTasks)
        {
            if (_cancellationTokens.TryRemove(entry.TaskId, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }

            if (entry.Process is not null)
            {
                try
                {
                    if (!entry.Process.HasExited) entry.Process.Kill();
                }
                catch (Exception ex)
                {
                    _logger?.LogDebug(ex, "杀死后台任务进程失败: {TaskId}", entry.TaskId);
                }
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

    public void Dispose()
    {
        foreach (var cts in _cancellationTokens.Values)
        {
            cts.Cancel();
            cts.Dispose();
        }

        _cancellationTokens.Clear();
        _tasks.Clear();
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
        public Process? Process { get; set; }
    }
}
