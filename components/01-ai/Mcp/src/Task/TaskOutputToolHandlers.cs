

namespace McpToolHandlers;

[McpToolHandler(ToolCategory.Task, Optional = true)]
public partial class TaskOutputToolHandlers
{
    private readonly ITaskService _taskService;
    [Inject] private readonly ILogger<TaskOutputToolHandlers>? _logger;

    public TaskOutputToolHandlers(ITaskService taskService, ILogger<TaskOutputToolHandlers>? logger = null)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
        _logger = logger;
    }

    [McpTool(SystemToolNameConstants.TaskOutput, "Get output result of a background task", "task")]
    public async Task<ToolResult> GetTaskOutputAsync(
        [McpToolParameter("Task ID")] string task_id,
        [McpToolParameter("Output type: stdout/stderr/all (optional, default all)", Required = false)] string? output_type = "all",
        [McpToolParameter("Maximum output lines (optional, default 100)", Required = false)] int? max_lines = 100,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task_id))
            return McpResultBuilder.Error().WithText(L.T(StringKey.TaskIdCannotBeEmpty)).Build();

        var outputType = TaskOutputTypeExtensions.FromValue(output_type ?? "all") ?? TaskOutputType.All;

        try
        {
            var task = await _taskService.GetTaskAsync(task_id, cancellationToken).ConfigureAwait(false);
            if (task == null)
                return McpResultBuilder.Error().WithText(L.T(StringKey.TaskNotFound, task_id)).Build();

            var response = new System.Text.StringBuilder();
            response.AppendLine(L.T(StringKey.TaskOutputTitle, task_id));
            response.AppendLine(L.T(StringKey.TaskLabelTitle, task.Title));
            response.AppendLine(L.T(StringKey.TaskLabelStatus, task.Status));
            response.AppendLine();

            var description = task.Description ?? string.Empty;
            var effectiveMaxLines = max_lines ?? 100;

            if (!string.IsNullOrEmpty(description))
            {
                var lines = description.Split('\n');
                var truncated = lines.Length > effectiveMaxLines;
                var displayLines = lines.Take(effectiveMaxLines);
                response.AppendLine(L.T(StringKey.TaskLabelOutput));
                response.Append(string.Join("\n", displayLines));
                if (truncated)
                    response.AppendLine(L.T(StringKey.TaskOutputTruncated, lines.Length, effectiveMaxLines));
                response.AppendLine();
            }
            else
            {
                response.AppendLine(L.T(StringKey.TaskNoOutput));
            }

            return McpResultBuilder.Success().WithText(response.ToString()).Build();
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, L.T(StringKey.TaskOutputFailedLog, task_id));
            return McpResultBuilder.Error().WithText(L.T(StringKey.TaskOutputFailed, ex.Message)).Build();
        }
    }
}
