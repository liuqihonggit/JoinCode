
namespace Services.Todo.ToolHandlers;

/// <summary>
/// 任务工具处理器 - 提供任务管理功能
/// </summary>
[McpToolDispatch(ToolCategory.Task)]
public class TaskToolHandlers
{
    private readonly ITaskService _taskService;

    public TaskToolHandlers(ITaskService taskService)
    {
        _taskService = taskService ?? throw new ArgumentNullException(nameof(taskService));
    }

    /// <summary>
    /// 创建任务
    /// </summary>
    [McpTool(TaskToolNameConstants.TaskCreate, "Create a new task", "task")]
    public async Task<ToolResult> TaskCreateAsync(
        [McpToolParameter("Task title")] string title,
        [McpToolParameter("Task description (optional)", Required = false)] string? description = null,
        [McpToolParameter("Assignee (optional)", Required = false)] string? assignee = null,
        [McpToolParameter("Due date (optional)", Required = false)] DateTime? due_date = null,
        [McpToolParameter("Priority: low, medium, high, default medium", Required = false, DefaultValue = TodoPriorityConstants.Medium)] string priority = TodoPriorityConstants.Medium,
        [McpToolParameter("Tag list (optional)", Required = false)] List<string>? tags = null,
        CancellationToken cancellationToken = default)
    {
        var command = new TaskCreateCommand(title, description, assignee, due_date, priority, tags);
        var validationError = ValidateCommand(command);
        if (validationError is not null)
        {
            return ToolResultBuilder.Error().WithText(validationError.FormattedMessage).WithDiagnostic(validationError).Build();
        }

        var result = await _taskService.CreateTaskAsync(
            command.Title,
            command.Description,
            command.Assignee,
            command.DueDate,
            command.Priority,
            command.Tags,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            var errorMsg = result.ErrorMessage ?? L.T(StringKey.VaultCreateTaskFailed);
            var diagnostic = ToolDiagnostic.Create("ServiceFailure", errorMsg,
                [new DiagnosticDetail("operation", "CreateTask")],
                ["检查 ITaskService 实现的日志以获取详细错误。"]);
            return ToolResultBuilder.Error().WithText(errorMsg).WithDiagnostic(diagnostic).Build();
        }

        var response = FormatTaskResponse(result.GetData() ?? throw new InvalidOperationException("Task data is null."), L.T(StringKey.VaultTaskCreated));
        return ToolResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 列出任务
    /// </summary>
    [McpTool(TaskToolNameConstants.TaskList, "List tasks with optional filters", "task")]
    public async Task<ToolResult> TaskListAsync(
        [McpToolParameter("Status filter (optional)", Required = false)] string? status = null,
        [McpToolParameter("Assignee filter (optional)", Required = false)] string? assignee = null,
        [McpToolParameter("Priority filter (optional)", Required = false)] string? priority = null,
        [McpToolParameter("Result count limit, default 20", Required = false, DefaultValue = "20")] int? limit = null,
        [McpToolParameter("Offset for pagination", Required = false)] int? offset = null,
        CancellationToken cancellationToken = default)
    {
        var command = new TaskListCommand(status, assignee, priority, limit, offset);

        var result = await _taskService.ListTasksAsync(
            command.Status,
            command.Assignee,
            command.Priority,
            command.Limit ?? 20,
            command.Offset ?? 0,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            var errorMsg = result.ErrorMessage ?? L.T(StringKey.VaultListTaskFailed);
            var diagnostic = ToolDiagnostic.Create("ServiceFailure", errorMsg,
                [new DiagnosticDetail("operation", "ListTask")],
                ["检查 ITaskService 实现的日志以获取详细错误。"]);
            return ToolResultBuilder.Error().WithText(errorMsg).WithDiagnostic(diagnostic).Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.VaultTaskList, result.TotalCount));
        response.AppendLine(L.T(StringKey.VaultDisplayRange, (command.Offset ?? 0) + 1, Math.Min((command.Offset ?? 0) + result.Tasks.Count, result.TotalCount)));
        response.AppendLine();

        foreach (var task in result.Tasks)
        {
            response.AppendLine(FormatTaskSummary(task));
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 更新任务
    /// </summary>
    [McpTool(TaskToolNameConstants.TaskUpdate, "Update task information", "task")]
    public async Task<ToolResult> TaskUpdateAsync(
        [McpToolOptions] TaskUpdateOptions options,
        CancellationToken cancellationToken = default)
    {
        var command = new TaskUpdateCommand(options.TaskId, options.Title, options.Description, options.Status, options.Assignee, options.DueDate, options.Priority, options.Tags);
        var validationError = ValidateCommand(command);
        if (validationError is not null)
        {
            return ToolResultBuilder.Error().WithText(validationError.FormattedMessage).WithDiagnostic(validationError).Build();
        }

        var result = await _taskService.UpdateTaskAsync(
            new UpdateTaskRequest
            {
                TaskId = command.TaskId,
                Title = command.Title,
                Description = command.Description,
                Status = command.Status,
                Assignee = command.Assignee,
                DueDate = command.DueDate,
                Priority = command.Priority,
                Tags = command.Tags,
            },
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            var errorMsg = result.ErrorMessage ?? L.T(StringKey.VaultUpdateTaskFailed);
            var diagnostic = ToolDiagnostic.Create("ServiceFailure", errorMsg,
                [new DiagnosticDetail("operation", "UpdateTask"), new DiagnosticDetail("taskId", command.TaskId)],
                ["确认 task_id 是否存在，可先调用 TaskList 获取已有任务的 ID。"]);
            return ToolResultBuilder.Error().WithText(errorMsg).WithDiagnostic(diagnostic).Build();
        }

        var response = FormatTaskResponse(result.GetData() ?? throw new InvalidOperationException("Task data is null."), L.T(StringKey.VaultTaskUpdated));
        return ToolResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 停止任务
    /// </summary>
    [McpTool(TaskToolNameConstants.TaskStop, "Stop a task", "task")]
    public async Task<ToolResult> TaskStopAsync(
        [McpToolParameter("Task ID")] string task_id,
        [McpToolParameter("Stop reason (optional)", Required = false)] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        var command = new TaskStopCommand(task_id, reason);
        var validationError = ValidateCommand(command);
        if (validationError is not null)
        {
            return ToolResultBuilder.Error().WithText(validationError.FormattedMessage).WithDiagnostic(validationError).Build();
        }

        var result = await _taskService.StopTaskAsync(
            command.TaskId,
            command.Reason,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            var errorMsg = result.ErrorMessage ?? L.T(StringKey.VaultStopTaskFailed);
            var diagnostic = ToolDiagnostic.Create("ServiceFailure", errorMsg,
                [new DiagnosticDetail("operation", "StopTask"), new DiagnosticDetail("taskId", command.TaskId)],
                ["确认 task_id 是否存在，可先调用 TaskList 获取已有任务的 ID。"]);
            return ToolResultBuilder.Error().WithText(errorMsg).WithDiagnostic(diagnostic).Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.VaultTaskStopped, command.TaskId));

        if (!string.IsNullOrEmpty(command.Reason))
        {
            response.AppendLine(L.T(StringKey.VaultLabelReason, command.Reason));
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取任务详情
    /// </summary>
    [McpTool(TaskToolNameConstants.TaskGet, "Get task details", "task")]
    public async Task<ToolResult> TaskGetAsync(
        [McpToolParameter("Task ID")] string task_id,
        CancellationToken cancellationToken = default)
    {
        var command = new TaskGetCommand(task_id);
        var validationError = ValidateCommand(command);
        if (validationError is not null)
        {
            return ToolResultBuilder.Error().WithText(validationError.FormattedMessage).WithDiagnostic(validationError).Build();
        }

        var task = await _taskService.GetTaskAsync(command.TaskId, cancellationToken).ConfigureAwait(false);

        if (task == null)
        {
            var errorMsg = L.T(StringKey.VaultTaskNotFound, command.TaskId);
            var diagnostic = ToolDiagnostic.Create("TaskNotFound", errorMsg,
                [new DiagnosticDetail("taskId", command.TaskId)],
                ["确认 task_id 是否存在，可先调用 TaskList 获取已有任务的 ID。"]);
            return ToolResultBuilder.Error().WithText(errorMsg).WithDiagnostic(diagnostic).Build();
        }

        var response = FormatTaskResponse(task, L.T(StringKey.VaultTaskDetails));
        return ToolResultBuilder.Success().WithText(response).Build();
    }

    /// <summary>
    /// 设置任务依赖关系
    /// </summary>
    [McpTool(TaskToolNameConstants.TaskSetDependency, "Set a task dependency", "task")]
    public async Task<ToolResult> TaskSetDependencyAsync(
        [McpToolParameter("Task ID")] string task_id,
        [McpToolParameter("Depends-on task ID")] string depends_on_task_id,
        [McpToolParameter("Dependency type: blocks, soft, subtask, default blocks", Required = false, DefaultValue = "blocks")] string? dependency_type = null,
        CancellationToken cancellationToken = default)
    {
        var command = new TaskSetDependencyCommand(task_id, depends_on_task_id, dependency_type);
        var validationError = ValidateCommand(command);
        if (validationError is not null)
        {
            return ToolResultBuilder.Error().WithText(validationError.FormattedMessage).WithDiagnostic(validationError).Build();
        }

        var dependencyType = ParseDependencyType(command.DependencyType);

        var result = await _taskService.SetTaskDependencyAsync(
            command.TaskId,
            command.DependsOnTaskId,
            dependencyType,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            var errorMsg = result.ErrorMessage ?? L.T(StringKey.VaultSetDependencyFailed);
            var diagnostic = ToolDiagnostic.Create("ServiceFailure", errorMsg,
                [new DiagnosticDetail("operation", "SetTaskDependency"), new DiagnosticDetail("taskId", command.TaskId), new DiagnosticDetail("dependsOnTaskId", command.DependsOnTaskId)],
                ["确认两个 task_id 是否存在，或检查是否已存在相同依赖关系。"]);
            return ToolResultBuilder.Error().WithText(errorMsg).WithDiagnostic(diagnostic).Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.VaultTaskDependencySet));
        response.AppendLine(L.T(StringKey.VaultLabelTask, command.TaskId));
        response.AppendLine(L.T(StringKey.VaultLabelDependsOn, command.DependsOnTaskId));
        response.AppendLine(L.T(StringKey.VaultLabelDependencyType, dependencyType));

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 移除任务依赖关系
    /// </summary>
    [McpTool(TaskToolNameConstants.TaskRemoveDependency, "Remove a task dependency", "task")]
    public async Task<ToolResult> TaskRemoveDependencyAsync(
        [McpToolParameter("Task ID")] string task_id,
        [McpToolParameter("Depends-on task ID")] string depends_on_task_id,
        CancellationToken cancellationToken = default)
    {
        var command = new TaskRemoveDependencyCommand(task_id, depends_on_task_id);
        var validationError = ValidateCommand(command);
        if (validationError is not null)
        {
            return ToolResultBuilder.Error().WithText(validationError.FormattedMessage).WithDiagnostic(validationError).Build();
        }

        var result = await _taskService.RemoveTaskDependencyAsync(
            command.TaskId,
            command.DependsOnTaskId,
            cancellationToken).ConfigureAwait(false);

        if (!result.Success)
        {
            var errorMsg = result.ErrorMessage ?? L.T(StringKey.VaultRemoveDependencyFailed);
            var diagnostic = ToolDiagnostic.Create("ServiceFailure", errorMsg,
                [new DiagnosticDetail("operation", "RemoveTaskDependency"), new DiagnosticDetail("taskId", command.TaskId), new DiagnosticDetail("dependsOnTaskId", command.DependsOnTaskId)],
                ["确认两个 task_id 是否存在，或检查依赖关系是否已建立。"]);
            return ToolResultBuilder.Error().WithText(errorMsg).WithDiagnostic(diagnostic).Build();
        }

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.VaultTaskDependencyRemoved));
        response.AppendLine(L.T(StringKey.VaultLabelTask, command.TaskId));
        response.AppendLine(L.T(StringKey.VaultLabelDependsOn, command.DependsOnTaskId));

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取任务依赖列表
    /// </summary>
    [McpTool(TaskToolNameConstants.TaskGetDependencies, "Get task dependency list", "task")]
    public async Task<ToolResult> TaskGetDependenciesAsync(
        [McpToolParameter("Task ID")] string task_id,
        CancellationToken cancellationToken = default)
    {
        var command = new TaskGetDependenciesCommand(task_id);
        var validationError = ValidateCommand(command);
        if (validationError is not null)
        {
            return ToolResultBuilder.Error().WithText(validationError.FormattedMessage).WithDiagnostic(validationError).Build();
        }

        var dependencies = await _taskService.GetTaskDependenciesAsync(command.TaskId, cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.VaultTaskDependencyList, command.TaskId));
        response.AppendLine();

        if (dependencies.Count == 0)
        {
            response.AppendLine(L.T(StringKey.VaultNoDependencies));
        }
        else
        {
            response.Append(string.Join(Environment.NewLine,
                dependencies.Select(dep =>
                    $"{dep.DependencyType switch { TaskDependencyType.Blocks => StatusSymbol.Prohibited.ToValue(), TaskDependencyType.Soft => ObjectSymbol.ArrowRight.ToValue(), TaskDependencyType.Subtask => ObjectSymbol.List.ToValue(), _ => StatusSymbol.Info.ToValue() }} {dep.DependsOnTaskId} ({dep.DependencyType})")));
            response.AppendLine();
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 检查任务是否可以执行
    /// </summary>
    [McpTool(TaskToolNameConstants.TaskCanExecute, "Check if a task can execute (dependencies satisfied)", "task")]
    public async Task<ToolResult> TaskCanExecuteAsync(
        [McpToolParameter("Task ID")] string task_id,
        CancellationToken cancellationToken = default)
    {
        var command = new TaskCanExecuteCommand(task_id);
        var validationError = ValidateCommand(command);
        if (validationError is not null)
        {
            return ToolResultBuilder.Error().WithText(validationError.FormattedMessage).WithDiagnostic(validationError).Build();
        }

        var canExecute = await _taskService.CanExecuteTaskAsync(command.TaskId, cancellationToken).ConfigureAwait(false);

        var response = new System.Text.StringBuilder();
        response.AppendLine(L.T(StringKey.VaultTaskExecutionCheck, command.TaskId));
        response.AppendLine();

        if (canExecute)
        {
            response.AppendLine(L.T(StringKey.VaultTaskCanExecute, StatusSymbol.Tick.ToValue()));
            response.AppendLine(L.T(StringKey.VaultAllDependenciesSatisfied));
        }
        else
        {
            response.AppendLine(L.T(StringKey.VaultTaskCannotExecute, StatusSymbol.Cross.ToValue()));
            response.AppendLine(L.T(StringKey.VaultPossibleReasons));
            response.AppendLine(L.T(StringKey.VaultReasonTaskNotExist));
            response.AppendLine(L.T(StringKey.VaultReasonInvalidStatus));
            response.AppendLine(L.T(StringKey.VaultReasonBlockingDependency));
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    #region Diagnostic Builders

    /// <summary>
    /// task_id 为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildEmptyTaskIdDiagnostic()
    {
        return ToolDiagnostic.Create("EmptyTaskId", L.T(StringKey.VaultTaskIdCannotBeEmpty),
            [new DiagnosticDetail("field", "task_id")],
            ["提供非空的 task_id，可先调用 TaskList 获取已有任务的 ID。"]);
    }

    /// <summary>
    /// 通用字段为空的结构化诊断。
    /// </summary>
    internal static ToolDiagnostic BuildEmptyFieldDiagnostic(string reason, string fieldName, string message)
    {
        return ToolDiagnostic.Create(reason, message,
            [new DiagnosticDetail("field", fieldName)],
            [$"提供非空的 {fieldName} 字段。"]);
    }

    #endregion

    #region Private Methods

    private static ToolDiagnostic? ValidateCommand<TCommand>(TCommand command)
    {
        return command switch
        {
            TaskCreateCommand cmd => string.IsNullOrWhiteSpace(cmd.Title) ? BuildEmptyFieldDiagnostic("EmptyTitle", "title", L.T(StringKey.VaultTitleCannotBeEmpty)) : null,
            TaskUpdateCommand cmd => string.IsNullOrWhiteSpace(cmd.TaskId) ? BuildEmptyTaskIdDiagnostic() : null,
            TaskStopCommand cmd => string.IsNullOrWhiteSpace(cmd.TaskId) ? BuildEmptyTaskIdDiagnostic() : null,
            TaskGetCommand cmd => string.IsNullOrWhiteSpace(cmd.TaskId) ? BuildEmptyTaskIdDiagnostic() : null,
            TaskSetDependencyCommand cmd => string.IsNullOrWhiteSpace(cmd.TaskId) ? BuildEmptyTaskIdDiagnostic()
                : string.IsNullOrWhiteSpace(cmd.DependsOnTaskId) ? BuildEmptyFieldDiagnostic("EmptyDependsOnTaskId", "depends_on_task_id", L.T(StringKey.VaultDependsOnTaskIdCannotBeEmpty)) : null,
            TaskRemoveDependencyCommand cmd => string.IsNullOrWhiteSpace(cmd.TaskId) ? BuildEmptyTaskIdDiagnostic()
                : string.IsNullOrWhiteSpace(cmd.DependsOnTaskId) ? BuildEmptyFieldDiagnostic("EmptyDependsOnTaskId", "depends_on_task_id", L.T(StringKey.VaultDependsOnTaskIdCannotBeEmpty)) : null,
            TaskGetDependenciesCommand cmd => string.IsNullOrWhiteSpace(cmd.TaskId) ? BuildEmptyTaskIdDiagnostic() : null,
            TaskCanExecuteCommand cmd => string.IsNullOrWhiteSpace(cmd.TaskId) ? BuildEmptyTaskIdDiagnostic() : null,
            _ => null
        };
    }

    private static TaskDependencyType ParseDependencyType(string? type)
    {
        return TaskDependencyTypeExtensions.FromValue(type) ?? TaskDependencyType.Blocks;
    }

    private static string FormatTaskResponse(TaskItem task, string header)
    {
        var response = new System.Text.StringBuilder();
        response.AppendLine($"{header}");
        response.AppendLine($"ID: {task.Id}");
        response.AppendLine(L.T(StringKey.VaultLabelTitle, task.Title));

        if (!string.IsNullOrEmpty(task.Description))
        {
            response.AppendLine(L.T(StringKey.VaultLabelDescription, task.Description));
        }

        response.AppendLine(L.T(StringKey.VaultLabelStatus, task.Status));
        response.AppendLine(L.T(StringKey.VaultLabelPriority, task.Priority));

        if (!string.IsNullOrEmpty(task.Assignee))
        {
            response.AppendLine(L.T(StringKey.VaultLabelAssignee, task.Assignee));
        }

        if (task.DueDate.HasValue)
        {
            response.AppendLine(L.T(StringKey.VaultLabelDueDate, task.DueDate.Value.ToString("yyyy-MM-dd")));
        }

        response.AppendLine(L.T(StringKey.VaultLabelCreatedAt, task.CreatedAt.ToString("yyyy-MM-dd HH:mm")));

        if (task.Tags.Count > 0)
        {
            response.AppendLine(L.T(StringKey.VaultLabelTags, string.Join(", ", task.Tags)));
        }

        return response.ToString();
    }

    private static string FormatTaskSummary(TaskItem task)
    {
        var priorityIcon = TodoIcons.PriorityIcons.GetValueOrDefault(task.Priority.ToValue(), "⚪");
        var statusIcon = TodoIcons.TaskStatusIcons.GetValueOrDefault(task.Status, StatusSymbol.Info.ToValue());

        var sb = new StringBuilder();
        sb.Append(statusIcon).Append(' ').Append(priorityIcon).Append(" [").Append(task.Id).Append("] ").Append(task.Title);

        if (!string.IsNullOrEmpty(task.Assignee))
        {
            sb.Append(L.T(StringKey.VaultSummaryAssignee, task.Assignee));
        }

        if (task.DueDate.HasValue)
        {
            sb.Append(L.T(StringKey.VaultSummaryDueDate, task.DueDate.Value.ToString("MM-dd")));
        }

        return sb.ToString();
    }

    #endregion
}
