
namespace Services.Todo.ToolHandlers;

[McpToolHandler(ToolCategory.Todo)]
public class TodoToolHandlers
{
    private readonly ITodoService _todoService;

    public TodoToolHandlers(ITodoService todoService)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
    }

    [McpTool(TodoToolNameConstants.TodoWrite, "Update the todo list for the current session. To be used proactively and often to track progress and pending tasks. Make sure that at least one task is in_progress at all times. Always provide both content (imperative) and activeForm (present continuous) for each task.", "todo")]
    public async Task<ToolResult> TodoWriteAsync(
        [McpToolParameter("The updated todo list. Each item has: content (required), status (pending/in_progress/completed, required), activeForm (required, present tense like 'Implementing feature'), priority (high/medium/low, optional), id (optional, auto-generated if omitted)", Required = false)] List<TodoItemInput>? todos = null,
        CancellationToken cancellationToken = default)
    {
        var todoInputs = todos ?? [];

        foreach (var item in todoInputs)
        {
            if (string.IsNullOrWhiteSpace(item.Content))
            {
                return McpResultBuilder.Error().WithText("Todo item content cannot be empty").Build();
            }

            if (!TodoIcons.ValidTodoStatuses.Contains(item.Status))
            {
                return McpResultBuilder.Error().WithText($"Invalid status '{item.Status}'. Must be one of: pending, in_progress, completed").Build();
            }

            if (!string.IsNullOrEmpty(item.Priority) && TodoPriorityExtensions.FromValue(item.Priority) is null)
            {
                return McpResultBuilder.Error().WithText($"Invalid priority '{item.Priority}'. Must be one of: high, medium, low").Build();
            }
        }

        // 对齐 TS: id 和 priority 为可选字段，缺失时自动填充
        for (var i = 0; i < todoInputs.Count; i++)
        {
            if (string.IsNullOrEmpty(todoInputs[i].Id))
            {
                todoInputs[i] = todoInputs[i] with { Id = $"todo_{Guid.NewGuid():N}" };
            }
            if (string.IsNullOrEmpty(todoInputs[i].Priority))
            {
                todoInputs[i] = todoInputs[i] with { Priority = TodoPriorityConstants.Medium };
            }
        }

        var allDone = todoInputs.Count > 0 && todoInputs.All(t =>
            t.Status.Equals(TodoStatusConstants.Completed, StringComparison.OrdinalIgnoreCase));

        if (allDone)
        {
            await _todoService.WriteTodosAsync(todoInputs, cancellationToken).ConfigureAwait(false);
            await _todoService.ClearTodosAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var result = await _todoService.WriteTodosAsync(
                todoInputs,
                cancellationToken).ConfigureAwait(false);

            if (!result.Success)
            {
                return McpResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to write todos").Build();
            }
        }

        var response = new StringBuilder();
        response.Append("Todos have been successfully written. Ensure that you use the todo list to track your progress. Please proceed with the current task as applicable");

        var verificationNudgeNeeded = allDone && todoInputs.Count >= 3 &&
            !todoInputs.Any(t => t.Content.Contains("verif", StringComparison.OrdinalIgnoreCase));

        if (verificationNudgeNeeded)
        {
            response.AppendLine();
            response.AppendLine();
            response.Append("NOTE: You just closed out 3+ tasks and none of them was a verification step. Before writing your final summary, spawn the verification agent (subagent_type=\"verification\"). You cannot self-assign PARTIAL by listing caveats in your summary — only the verifier issues a verdict.");
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    [McpTool(TodoToolNameConstants.TodoList, "List todo items with optional filtering", "todo")]
    public async Task<ToolResult> TodoListAsync(
        [McpToolParameter("Filter by status: pending, in_progress, completed", Required = false)] string? status = null,
        [McpToolParameter("Filter by priority: low, medium, high", Required = false)] string? priority = null,
        [McpToolParameter("Whether to include completed todos (default: false)", Required = false, DefaultValue = "false")] bool include_completed = false,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(status) && !TodoIcons.ValidTodoStatuses.Contains(status))
        {
            return McpResultBuilder.Error().WithText($"Invalid status filter '{status}'. Must be one of: pending, in_progress, completed").Build();
        }

        if (!string.IsNullOrEmpty(priority) && TodoPriorityExtensions.FromValue(priority) is null)
        {
            return McpResultBuilder.Error().WithText($"Invalid priority filter '{priority}'. Must be one of: high, medium, low").Build();
        }

        var result = await _todoService.ListTodosAsync(
            status,
            priority,
            include_completed,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to list todos").Build();
        }

        var response = new StringBuilder();
        response.AppendLine($"Todo Statistics");
        response.AppendLine($"Total: {result.TotalCount}");
        response.AppendLine($"Pending: {result.PendingCount}");
        response.AppendLine($"Completed: {result.CompletedCount}");

        if (result.Todos.Count > 0)
        {
            response.AppendLine();
            response.AppendLine("Todo List:");
            response.Append(string.Join(Environment.NewLine, result.Todos.Select(FormatTodoSummary)));
            response.AppendLine();
        }
        else
        {
            response.AppendLine();
            response.AppendLine("No todo items found");
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    [McpTool(TodoToolNameConstants.TodoUpdate, "Update a single todo item", "todo")]
    public async Task<ToolResult> TodoUpdateAsync(
        [McpToolParameter("The ID of the todo item to update")] string todo_id,
        [McpToolParameter("New content (optional)", Required = false)] string? content = null,
        [McpToolParameter("New status: pending, in_progress, completed (optional)", Required = false)] string? status = null,
        [McpToolParameter("New priority: low, medium, high (optional)", Required = false)] string? priority = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(todo_id))
        {
            return McpResultBuilder.Error().WithText("todo_id cannot be empty").Build();
        }

        if (!string.IsNullOrEmpty(status) && !TodoIcons.ValidTodoStatuses.Contains(status))
        {
            return McpResultBuilder.Error().WithText($"Invalid status '{status}'. Must be one of: pending, in_progress, completed").Build();
        }

        if (!string.IsNullOrEmpty(priority) && TodoPriorityExtensions.FromValue(priority) is null)
        {
            return McpResultBuilder.Error().WithText($"Invalid priority '{priority}'. Must be one of: high, medium, low").Build();
        }

        var result = await _todoService.UpdateTodoAsync(
            todo_id,
            content,
            status,
            priority,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            return McpResultBuilder.Error().WithText(result.ErrorMessage ?? "Failed to update todo").Build();
        }

        var response = new StringBuilder();
        response.Append("Todo item updated successfully");
        if (result.Todo != null)
        {
            response.AppendLine();
            response.Append(FormatTodoSummary(result.Todo));
        }

        return McpResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #region Private Methods

    private static string FormatTodoSummary(TodoItem todo)
    {
        var priorityIcon = TodoIcons.PriorityIcons.GetValueOrDefault(todo.Priority, "⚪");
        var statusIcon = TodoIcons.TodoStatusIcons.GetValueOrDefault(todo.Status, StatusSymbol.Info.ToValue());

        var sb = new StringBuilder();
        sb.Append(statusIcon).Append(' ').Append(priorityIcon).Append(" [").Append(todo.Id).Append("] ").Append(todo.Content);

        if (!string.IsNullOrEmpty(todo.ActiveForm))
        {
            sb.Append(" (").Append(todo.ActiveForm).Append(')');
        }

        return sb.ToString();
    }

    #endregion
}
