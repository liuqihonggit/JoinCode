namespace McpToolDispatch;

/// <summary>
/// 任务停止工具处理器 — 提供停止后台任务、批量停止、列出运行中任务等功能
/// </summary>
[McpToolDispatch(ToolCategory.Task, Optional = true)]
public partial class TaskStopToolHandlers {
    private readonly ITaskService _taskService;
    private readonly IAgentService _agentCoordinator;
    private readonly ILogger<TaskStopToolHandlers>? _logger;

    /// <summary>
    /// 初始化 <see cref="TaskStopToolHandlers"/> 实例
    /// </summary>
    /// <param name="taskService">任务服务</param>
    /// <param name="agentCoordinator">智能体协调服务</param>
    /// <param name="logger">日志记录器（可选）</param>
    public TaskStopToolHandlers(
        ITaskService taskService,
        IAgentService agentCoordinator,
        ILogger<TaskStopToolHandlers>? logger = null) {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _agentCoordinator = agentCoordinator ?? throw new ArgumentNullException(nameof(agentCoordinator));
        _logger = logger;
    }

    /// <summary>
    /// 按 ID 停止运行中的后台任务
    /// </summary>
    /// <param name="task_id">要停止的后台任务 ID</param>
    /// <param name="shell_id">已废弃：请使用 task_id（KillShell 兼容）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(TaskToolNameEnumConstants.TaskStop, "Stop a running background task by ID", "task")]
    public async Task<ToolResult> StopTaskAsync(
        [McpToolParameter("The ID of the background task to stop")] string? task_id = null,
        [McpToolParameter("Deprecated: use task_id instead (KillShell compat)", Required = false)] string? shell_id = null,
        CancellationToken cancellationToken = default) {
        var id = task_id ?? shell_id;
        if (string.IsNullOrWhiteSpace(id))
            return ToolResultBuilder.Error().WithText("Missing required parameter: task_id").Build();

        try {
            var taskMatch = await _taskService.GetRunningTaskByIdAsync(id, cancellationToken).ConfigureAwait(false);
            var agentMatch = await _agentCoordinator.GetRunningAgentByIdAsync(id, cancellationToken).ConfigureAwait(false);

            if (taskMatch is null && agentMatch is null)
                return ToolResultBuilder.Error().WithText($"No task found with ID: {id}").Build();

            string? taskType = null;
            string? command = null;

            if (taskMatch is not null) {
                taskType = "task";
                command = taskMatch.Description;
                await _taskService.StopTaskAsync(id, false, cancellationToken).ConfigureAwait(false);
            }

            if (agentMatch is not null) {
                taskType = "agent";
                command = agentMatch.Description;
                await _agentCoordinator.StopAgentAsync(id, cancellationToken).ConfigureAwait(false);
            }

            _logger?.LogInformation("Stopped task {TaskId} (type: {TaskType})", id, taskType);

            var output = new TaskStopOutput(
                Message: $"Successfully stopped task: {id} ({command})",
                TaskId: id,
                TaskType: taskType ?? "unknown",
                Command: command
            );

            return ToolResultBuilder.Success()
                .WithText(System.Text.Json.JsonSerializer.Serialize(output, TaskStopOutputContext.Default.TaskStopOutput))
                .Build();
        } catch (OperationCanceledException) { throw; } catch (Exception ex) {
            _logger?.LogError(ex, "Failed to stop task {TaskId}", id);
            return ToolExceptionDiagnosticHelper.BuildErrorResult("task_stop", ex, _logger, "task_id", id);
        }
    }

    /// <summary>
    /// 批量停止多个运行中任务
    /// </summary>
    /// <param name="task_ids">逗号分隔的任务 ID 列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(TaskToolNameEnumConstants.TaskStopBatch, "Stop multiple running tasks", "task")]
    public async Task<ToolResult> StopTasksBatchAsync(
        [McpToolParameter("Comma-separated task IDs")] string task_ids,
        CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(task_ids))
            return ToolResultBuilder.Error().WithText("task_ids cannot be empty").Build();

        var ids = task_ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(static id => id.Trim())
            .Where(static id => !string.IsNullOrEmpty(id))
            .ToList();

        if (ids.Count == 0)
            return ToolResultBuilder.Error().WithText("No valid task IDs provided").Build();

        var tasks = ids.Select(async id => {
            try {
                var taskStopped = await _taskService.StopTaskAsync(id, false, cancellationToken).ConfigureAwait(false);
                var agentStopped = await _agentCoordinator.StopAgentAsync(id, cancellationToken).ConfigureAwait(false);
                return (Id: id, Success: taskStopped || agentStopped, Detail: taskStopped || agentStopped ? "stopped" : "not found");
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                return (Id: id, Success: false, Detail: $"error: [{ex.GetType().Name}] {ex.Message}");
            }
        });
        var results = (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Batch stop result ({results.Count(r => r.Success)}/{results.Count} succeeded):");
        sb.AppendLine();
        sb.Append(string.Join(Environment.NewLine, results.Select(r => {
            var icon = r.Success ? "✓" : "✗";
            return $"{icon} {r.Id}: {r.Detail}";
        })));

        var builder = results.All(r => r.Success) ? ToolResultBuilder.Success() : ToolResultBuilder.Error();
        return builder.WithText(sb.ToString()).Build();
    }

    /// <summary>
    /// 列出所有运行中任务
    /// </summary>
    /// <param name="type">类型过滤：task/agent/all（默认 all）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果</returns>
    [McpTool(TaskToolNameEnumConstants.TaskListRunning, "List all running tasks", "task")]
    public async Task<ToolResult> ListRunningTasksAsync(
        [McpToolParameter("Filter by type: task/agent/all", Required = false)] string? type = "all",
        CancellationToken cancellationToken = default) {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Running tasks:");
        sb.AppendLine();

        var hasAny = false;

        if (type is "all" or "task") {
            var tasks = await _taskService.GetRunningTasksAsync(cancellationToken).ConfigureAwait(false);
            if (tasks.Count > 0) {
                hasAny = true;
                sb.AppendLine("[Tasks]");
                foreach (var task in tasks) {
                    sb.AppendLine($"- {task.Id}: {task.Description}");
                    sb.AppendLine($"  Status: {task.Status}");
                    AppendDuration(sb, task.StartedAt);
                    sb.AppendLine();
                }
            }
        }

        if (type is "all" or AgentToolNameEnumConstants.Agent) {
            var agents = await _agentCoordinator.GetRunningAgentsAsync(cancellationToken).ConfigureAwait(false);
            var agentList = agents.ToList();
            if (agentList.Count > 0) {
                hasAny = true;
                sb.AppendLine("[Agents]");
                foreach (var agent in agentList) {
                    sb.AppendLine($"- {agent.Id}: {agent.Description}");
                    var typeStr = agent.Variant.HasValue ? agent.Variant.Value.ToValue() : agent.Role.ToValue();
                    sb.AppendLine($"  Type: {typeStr ?? "general"}");
                    AppendDuration(sb, agent.StartedAt);
                    sb.AppendLine();
                }
            }
        }

        if (!hasAny)
            sb.AppendLine("No running tasks or agents");

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>
    /// 追加任务/Agent 的运行时长到输出 — 仅当 StartedAt 有值时输出
    /// </summary>
    private static void AppendDuration(System.Text.StringBuilder sb, DateTimeOffset? startedAt) {
        if (!startedAt.HasValue) return;
        var duration = DateTime.UtcNow - startedAt.Value;
        sb.AppendLine($"  Duration: {duration.TotalMinutes:F1} min");
    }
}

internal sealed record TaskStopOutput(
    string Message,
    string TaskId,
    string TaskType,
    string? Command
);