


namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Task, Optional = true)]
public partial class TaskStopToolHandlers
{
    private readonly ITaskService _taskService;
    private readonly IAgentCoordinator _agentCoordinator;
    [Inject] private readonly ILogger<TaskStopToolHandlers>? _logger;

    public TaskStopToolHandlers(
        ITaskService taskService,
        IAgentCoordinator agentCoordinator,
        ILogger<TaskStopToolHandlers>? logger = null)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _agentCoordinator = agentCoordinator ?? throw new ArgumentNullException(nameof(agentCoordinator));
        _logger = logger;
    }

    [McpTool(TaskToolNameConstants.TaskStop, "Stop a running background task by ID", "task")]
    public async Task<ToolResult> StopTaskAsync(
        [McpToolParameter("The ID of the background task to stop")] string? task_id = null,
        [McpToolParameter("Deprecated: use task_id instead (KillShell compat)", Required = false)] string? shell_id = null,
        CancellationToken cancellationToken = default)
    {
        var id = task_id ?? shell_id;
        if (string.IsNullOrWhiteSpace(id))
            return McpResultBuilder.Error().WithText("Missing required parameter: task_id").Build();

        try
        {
            var runningTasks = await _taskService.GetRunningTasksAsync(cancellationToken).ConfigureAwait(false);
            var runningAgents = await _agentCoordinator.GetRunningAgentsAsync(cancellationToken).ConfigureAwait(false);

            var taskMatch = runningTasks.FirstOrDefault(t => t.Id == id);
            var agentMatch = runningAgents.FirstOrDefault(a => a.Id == id);

            if (taskMatch is null && agentMatch is null)
                return McpResultBuilder.Error().WithText($"No task found with ID: {id}").Build();

            string? taskType = null;
            string? command = null;

            if (taskMatch is not null)
            {
                taskType = "task";
                command = taskMatch.Description;
                await _taskService.StopTaskAsync(id, false, cancellationToken).ConfigureAwait(false);
            }

            if (agentMatch is not null)
            {
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

            return McpResultBuilder.Success()
                .WithText(System.Text.Json.JsonSerializer.Serialize(output, TaskStopOutputContext.Default.TaskStopOutput))
                .Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to stop task {TaskId}", id);
            return McpResultBuilder.Error().WithText($"Failed to stop task: {ex.Message}").Build();
        }
    }

    [McpTool(TaskToolNameConstants.TaskStopBatch, "Stop multiple running tasks", "task")]
    public async Task<ToolResult> StopTasksBatchAsync(
        [McpToolParameter("Comma-separated task IDs")] string task_ids,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task_ids))
            return McpResultBuilder.Error().WithText("task_ids cannot be empty").Build();

        var ids = task_ids.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(static id => id.Trim())
            .Where(static id => !string.IsNullOrEmpty(id))
            .ToList();

        if (ids.Count == 0)
            return McpResultBuilder.Error().WithText("No valid task IDs provided").Build();

        var tasks = ids.Select(async id =>
        {
            try
            {
                var taskStopped = await _taskService.StopTaskAsync(id, false, cancellationToken).ConfigureAwait(false);
                var agentStopped = await _agentCoordinator.StopAgentAsync(id, cancellationToken).ConfigureAwait(false);
                return (Id: id, Success: taskStopped || agentStopped, Detail: taskStopped || agentStopped ? "stopped" : "not found");
            }
            catch (Exception ex)
            {
                return (Id: id, Success: false, Detail: $"error: {ex.Message}");
            }
        });
        var results = (await Task.WhenAll(tasks).ConfigureAwait(false)).ToList();

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Batch stop result ({results.Count(r => r.Success)}/{results.Count} succeeded):");
        sb.AppendLine();
        sb.Append(string.Join(Environment.NewLine, results.Select(r =>
        {
            var icon = r.Success ? "✓" : "✗";
            return $"{icon} {r.Id}: {r.Detail}";
        })));

        var builder = results.All(r => r.Success) ? McpResultBuilder.Success() : McpResultBuilder.Error();
        return builder.WithText(sb.ToString()).Build();
    }

    [McpTool(TaskToolNameConstants.TaskListRunning, "List all running tasks", "task")]
    public async Task<ToolResult> ListRunningTasksAsync(
        [McpToolParameter("Filter by type: task/agent/all", Required = false)] string? type = "all",
        CancellationToken cancellationToken = default)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Running tasks:");
        sb.AppendLine();

        var hasAny = false;

        if (type is "all" or "task")
        {
            var tasks = await _taskService.GetRunningTasksAsync(cancellationToken).ConfigureAwait(false);
            if (tasks.Count > 0)
            {
                hasAny = true;
                sb.AppendLine("[Tasks]");
                foreach (var task in tasks)
                {
                    sb.AppendLine($"- {task.Id}: {task.Description}");
                    sb.AppendLine($"  Status: {task.Status}");
                    if (task.StartedAt.HasValue)
                    {
                        var duration = DateTime.UtcNow - task.StartedAt.Value;
                        sb.AppendLine($"  Duration: {duration.TotalMinutes:F1} min");
                    }
                    sb.AppendLine();
                }
            }
        }

        if (type is "all" or AgentToolNameConstants.Agent)
        {
            var agents = await _agentCoordinator.GetRunningAgentsAsync(cancellationToken).ConfigureAwait(false);
            if (agents.Count > 0)
            {
                hasAny = true;
                sb.AppendLine("[Agents]");
                foreach (var agent in agents)
                {
                    sb.AppendLine($"- {agent.Id}: {agent.Description}");
                    sb.AppendLine($"  Type: {agent.AgentType ?? "general"}");
                    if (agent.StartedAt.HasValue)
                    {
                        var duration = DateTime.UtcNow - agent.StartedAt.Value;
                        sb.AppendLine($"  Duration: {duration.TotalMinutes:F1} min");
                    }
                    sb.AppendLine();
                }
            }
        }

        if (!hasAny)
            sb.AppendLine("No running tasks or agents");

        return McpResultBuilder.Success().WithText(sb.ToString()).Build();
    }
}

internal sealed record TaskStopOutput(
    string Message,
    string TaskId,
    string TaskType,
    string? Command
);
