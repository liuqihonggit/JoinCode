
namespace Services.Todo.ToolHandlers;

[McpToolDispatch(ToolCategory.Todo)]
public class TodoToolHandlers
{
    private readonly ITodoService _todoService;

    public TodoToolHandlers(ITodoService todoService)
    {
        _todoService = todoService ?? throw new ArgumentNullException(nameof(todoService));
    }

    [McpTool(TodoToolNameConstants.TodoWrite, "Update the todo list for the current session. To be used proactively and often to track progress and pending tasks. Make sure that at least one task is in_progress at all times. Always provide both content (imperative) and activeForm (present continuous) for each task. Supports dependsOn (list of todo IDs this task depends on) and ownedFiles (list of file paths this task owns) for DAG-based task planning.", "todo")]
    public async Task<ToolResult> TodoWriteAsync(
        [McpToolParameter("The updated todo list. Each item has: content (required), status (pending/in_progress/completed, required), activeForm (required, present tense like 'Implementing feature'), priority (high/medium/low, optional), id (optional, auto-generated if omitted), dependsOn (optional, list of todo IDs this task depends on), ownedFiles (optional, list of file paths this task owns)", Required = false)] List<TodoItemInput>? todos = null,
        CancellationToken cancellationToken = default)
    {
        var todoInputs = todos ?? [];

        for (var i = 0; i < todoInputs.Count; i++)
        {
            var item = todoInputs[i];
            if (string.IsNullOrWhiteSpace(item.Content))
            {
                var diagnostic = BuildEmptyContentDiagnostic(i);
                return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
            }

            if (!TodoIcons.ValidTodoStatuses.Contains(item.Status))
            {
                var diagnostic = BuildInvalidStatusDiagnostic(item.Status, i);
                return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
            }

            if (!string.IsNullOrEmpty(item.Priority) && TodoPriorityExtensions.FromValue(item.Priority) is null)
            {
                var diagnostic = BuildInvalidPriorityDiagnostic(item.Priority, i);
                return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
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
                var errorMsg = result.ErrorMessage ?? "Failed to write todos";
                var diagnostic = ToolDiagnostic.Create("ServiceFailure", errorMsg,
                    [new DiagnosticDetail("operation", "WriteTodos")],
                    ["检查 ITodoService 实现的日志以获取详细错误。"]);
                return ToolResultBuilder.Error().WithText(errorMsg).WithDiagnostic(diagnostic).Build();
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

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
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
            var diagnostic = BuildInvalidStatusFilterDiagnostic(status);
            return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
        }

        if (!string.IsNullOrEmpty(priority) && TodoPriorityExtensions.FromValue(priority) is null)
        {
            var diagnostic = BuildInvalidPriorityFilterDiagnostic(priority);
            return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
        }

        var result = await _todoService.ListTodosAsync(
            status,
            priority,
            include_completed,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            var errorMsg = result.ErrorMessage ?? "Failed to list todos";
            var diagnostic = ToolDiagnostic.Create("ServiceFailure", errorMsg,
                [new DiagnosticDetail("operation", "ListTodos")],
                ["检查 ITodoService 实现的日志以获取详细错误。"]);
            return ToolResultBuilder.Error().WithText(errorMsg).WithDiagnostic(diagnostic).Build();
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

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
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
            var diagnostic = BuildEmptyTodoIdDiagnostic();
            return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
        }

        if (!string.IsNullOrEmpty(status) && !TodoIcons.ValidTodoStatuses.Contains(status))
        {
            var diagnostic = BuildInvalidStatusDiagnostic(status);
            return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
        }

        if (!string.IsNullOrEmpty(priority) && TodoPriorityExtensions.FromValue(priority) is null)
        {
            var diagnostic = BuildInvalidPriorityDiagnostic(priority);
            return ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
        }

        var result = await _todoService.UpdateTodoAsync(
            todo_id,
            content,
            status,
            priority,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            var errorMsg = result.ErrorMessage ?? "Failed to update todo";
            var diagnostic = ToolDiagnostic.Create("ServiceFailure", errorMsg,
                [new DiagnosticDetail("operation", "UpdateTodo"), new DiagnosticDetail("todoId", todo_id)],
                ["确认 todo_id 是否存在，可先调用 TodoList 获取已有 todo 的 ID。"]);
            return ToolResultBuilder.Error().WithText(errorMsg).WithDiagnostic(diagnostic).Build();
        }

        var response = new StringBuilder();
        response.Append("Todo item updated successfully");
        if (result.Data != null)
        {
            response.AppendLine();
            response.Append(FormatTodoSummary(result.Data));
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #region Diagnostic Builders

    /// <summary>
    /// content 为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildEmptyContentDiagnostic(int itemIndex)
    {
        var sb = new StringBuilder(128);
        sb.Append("Todo item content cannot be empty");
        sb.Append($"\n[诊断] 出错位置: todos[{itemIndex}]");
        return ToolDiagnostic.Create("EmptyContent", sb.ToString(),
            [new DiagnosticDetail("itemIndex", itemIndex.ToString())],
            ["为每个 todo 项提供非空的 content 字段。"]);
    }

    /// <summary>
    /// 无效 status 的结构化诊断（TodoWrite/TodoUpdate 场景，itemIndex 仅 TodoWrite 有）。
    /// </summary>
    internal static ToolDiagnostic BuildInvalidStatusDiagnostic(string status, int? itemIndex = null)
    {
        const string validValues = "pending, in_progress, completed";
        var sb = new StringBuilder(160);
        sb.Append($"Invalid status '{status}'. Must be one of: {validValues}");
        return FinishInvalidEnumDiagnostic(sb, "InvalidStatus", status, validValues,
            TodoIcons.ValidTodoStatuses, itemIndex,
            "有效状态: pending（待开始）、in_progress（进行中）、completed（已完成）。");
    }

    /// <summary>
    /// 无效 priority 的结构化诊断（TodoWrite/TodoUpdate 场景，itemIndex 仅 TodoWrite 有）。
    /// </summary>
    internal static ToolDiagnostic BuildInvalidPriorityDiagnostic(string priority, int? itemIndex = null)
    {
        const string validValues = "high, medium, low";
        var sb = new StringBuilder(160);
        sb.Append($"Invalid priority '{priority}'. Must be one of: {validValues}");
        return FinishInvalidEnumDiagnostic(sb, "InvalidPriority", priority, validValues,
            TodoIcons.ValidPriorities, itemIndex,
            "有效优先级: low（低）、medium（中）、high（高）。");
    }

    /// <summary>
    /// 无效 status 筛选器的结构化诊断（TodoList 场景）。
    /// </summary>
    internal static ToolDiagnostic BuildInvalidStatusFilterDiagnostic(string status)
    {
        const string validValues = "pending, in_progress, completed";
        var sb = new StringBuilder(160);
        sb.Append($"Invalid status filter '{status}'. Must be one of: {validValues}");
        return FinishInvalidEnumDiagnostic(sb, "InvalidStatusFilter", status, validValues,
            TodoIcons.ValidTodoStatuses, null,
            "有效状态: pending（待开始）、in_progress（进行中）、completed（已完成）。");
    }

    /// <summary>
    /// 无效 priority 筛选器的结构化诊断（TodoList 场景）。
    /// </summary>
    internal static ToolDiagnostic BuildInvalidPriorityFilterDiagnostic(string priority)
    {
        const string validValues = "high, medium, low";
        var sb = new StringBuilder(160);
        sb.Append($"Invalid priority filter '{priority}'. Must be one of: {validValues}");
        return FinishInvalidEnumDiagnostic(sb, "InvalidPriorityFilter", priority, validValues,
            TodoIcons.ValidPriorities, null,
            "有效优先级: low（低）、medium（中）、high（高）。");
    }

    /// <summary>
    /// todo_id 为空的结构化诊断（TodoUpdate 场景）。
    /// </summary>
    internal static ToolDiagnostic BuildEmptyTodoIdDiagnostic()
    {
        return ToolDiagnostic.Create("EmptyTodoId", "todo_id cannot be empty",
            [],
            ["提供非空的 todo_id，可先调用 TodoList 获取已有 todo 的 ID。"]);
    }

    private static ToolDiagnostic FinishInvalidEnumDiagnostic(
        StringBuilder sb, string reason, string input, string validValuesDisplay,
        FrozenSet<string> validValues, int? itemIndex, string validValuesDescription)
    {
        var details = new List<DiagnosticDetail>(4)
        {
            new("input", input),
            new("validValues", validValuesDisplay),
        };
        var suggestions = new List<string>(2);
        if (itemIndex.HasValue)
        {
            details.Add(new DiagnosticDetail("itemIndex", itemIndex.Value.ToString()));
            sb.Append($"\n[诊断] 出错位置: todos[{itemIndex.Value}]");
        }
        var candidate = SuggestValue(input, validValues);
        if (candidate is not null)
        {
            sb.Append($"\n[诊断] 你是不是想用: {candidate}");
            details.Add(new DiagnosticDetail("candidate", candidate));
            suggestions.Add($"你是不是想用: {candidate}");
        }
        suggestions.Add(validValuesDescription);
        return ToolDiagnostic.Create(reason, sb.ToString(), details, suggestions);
    }

    private static string? SuggestValue(string input, FrozenSet<string> validValues)
    {
        if (string.IsNullOrEmpty(input)) return null;
        foreach (var valid in validValues)
        {
            if (valid.Contains(input, StringComparison.OrdinalIgnoreCase) ||
                input.Contains(valid, StringComparison.OrdinalIgnoreCase))
            {
                return valid;
            }
        }
        return null;
    }

    #endregion

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
