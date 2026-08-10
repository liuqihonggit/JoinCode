namespace Tools.Handlers;

/// <summary>
/// Shell 执行工具处理器 — 提供 Bash 命令执行 + 后台任务管理
/// 通过中间件管道处理验证、分类、sed拦截、后台判断、执行、输出格式化
/// </summary>
[McpToolDispatch(ToolCategory.Shell)]
public partial class ShellToolHandlers : ShellToolBase
{
    private readonly MiddlewarePipeline<ShellPipelineContext> _pipeline;
    private readonly ISystemActuatorRegistry _registry;
    private readonly IFileSystem _fs;
    private readonly ILogger? _logger;

    public override string ToolName => ShellToolNameConstants.Bash;

    public ShellToolHandlers(
        MiddlewarePipeline<ShellPipelineContext> pipeline,
        ISystemActuatorRegistry registry,
        IFileSystem fs,
        ILogger? logger = null,
        IShellToolGateService? gateService = null,
        IShellProcessWatchdog? watchdog = null)
        : base(gateService, watchdog)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
    }

    /// <summary>
    /// 执行 Bash 命令
    /// </summary>
    [McpTool(ShellToolNameConstants.Bash, "Execute a Bash/CMD command. The description parameter briefly describes the command purpose", "execution")]
    public async Task<ToolResult> ShellExecuteAsync(
        [McpToolParameter("CMD command to execute. IMPORTANT: For search commands (rg/grep/find/ag), NEVER use --no-ignore/-u flags (bypasses .gitignore, may hang). NEVER search system root paths (C:\\, /, /home, C:\\Users). Always specify a project subdirectory as the search path.")] string command,
        [McpToolParameter("Brief description of the command purpose", Required = false)] string? description = null,
        [McpToolParameter("Timeout in milliseconds, default 120000ms", Required = false, DefaultValue = "120000")] int? timeout = null,
        [McpToolParameter("Working directory, defaults to current directory", Required = false)] string? working_directory = null,
        [McpToolParameter("Run in background (do not wait for completion)", Required = false, DefaultValue = "false")] bool? background = null,
        [McpToolParameter("Enable auto-backgrounding on timeout", Required = false, DefaultValue = "true")] bool? auto_background = null,
        [McpToolParameter("Override sandbox mode for this command", Required = false, DefaultValue = "false")] bool? dangerously_disable_sandbox = null,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        var actuator = _registry.Get(SystemActuatorKind.Bash);

        var context = new ShellPipelineContext
        {
            Command = command,
            Provider = actuator,
            Description = description,
            Timeout = timeout,
            TimeoutPolicy = TimeoutPolicy,
            WorkingDirectory = working_directory,
            Background = background,
            AutoBackground = auto_background,
            DangerouslyDisableSandbox = dangerously_disable_sandbox,
            CancellationToken = cancellationToken,
            OnProgress = onProgress,
        };

        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.Result ?? ToolResultBuilder.PipelineNoResult();
    }

    /// <summary>
    /// 获取后台任务状态
    /// </summary>
    [McpTool(ShellToolNameConstants.ShellBackgroundGet, "Get background shell task status", "execution", ConcurrencySafe = true)]
    public async Task<ToolResult> ShellBackgroundGetAsync(
        [McpToolParameter("Task ID")] string task_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task_id))
        {
            var diag = BuildEmptyTaskIdDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var task = await _registry.GetTaskAsync(task_id, cancellationToken).ConfigureAwait(false);

        if (task == null)
        {
            var diag = BuildTaskNotFoundDiagnostic(task_id);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var response = new StringBuilder();
        response.AppendLine("Background task status");
        response.AppendLine();
        response.AppendLine($"Task ID: {task.TaskId}");
        response.AppendLine($"Command: {task.Command}");
        response.AppendLine($"Status: {FormatStatus(task.Status)}");
        response.AppendLine($"Created: {task.CreatedAt:yyyy-MM-dd HH:mm:ss}");

        if (task.StartedAt.HasValue)
            response.AppendLine($"Started: {task.StartedAt.Value:yyyy-MM-dd HH:mm:ss}");

        if (task.CompletedAt.HasValue)
            response.AppendLine($"Completed: {task.CompletedAt.Value:yyyy-MM-dd HH:mm:ss}");

        if (task.ExitCode.HasValue)
            response.AppendLine($"Exit code: {task.ExitCode}");

        if (!string.IsNullOrEmpty(task.ErrorMessage))
            response.AppendLine($"Error: {task.ErrorMessage}");

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 列出所有后台任务
    /// </summary>
    [McpTool(ShellToolNameConstants.ShellBackgroundList, "List all background shell tasks", "execution", ConcurrencySafe = true)]
    public async Task<ToolResult> ShellBackgroundListAsync(
        CancellationToken cancellationToken = default)
    {
        var tasks = await _registry.ListTasksAsync(cancellationToken).ConfigureAwait(false);

        var response = new StringBuilder();
        response.AppendLine($"Background tasks ({tasks.Count} total)");
        response.AppendLine();

        if (tasks.Count == 0)
        {
            response.AppendLine("No background tasks");
        }
        else
        {
            foreach (var task in tasks)
            {
                var statusIcon = task.Status.ToStatusSymbol().ToValue();

                response.AppendLine($"{statusIcon} [{task.TaskId}] {task.Command[..Math.Min(40, task.Command.Length)]}...");
                response.AppendLine($"   Status: {FormatStatus(task.Status)} | Created: {task.CreatedAt:MM-dd HH:mm}");
            }
        }

        return ToolResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取后台任务输出
    /// </summary>
    [McpTool(ShellToolNameConstants.ShellBackgroundOutput, "Get output of a background shell task", "execution", ConcurrencySafe = true)]
    public async Task<ToolResult> ShellBackgroundOutputAsync(
        [McpToolParameter("Task ID")] string task_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task_id))
        {
            var diag = BuildEmptyTaskIdDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var output = await _registry.GetTaskOutputAsync(task_id, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(output))
            return ToolResultBuilder.Success().WithText("(No output yet)").Build();

        return ToolResultBuilder.Success().WithText(output).Build();
    }

    /// <summary>
    /// 取消后台任务
    /// </summary>
    [McpTool(ShellToolNameConstants.ShellBackgroundCancel, "Cancel a background shell task", "execution")]
    public async Task<ToolResult> ShellBackgroundCancelAsync(
        [McpToolParameter("Task ID")] string task_id,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(task_id))
        {
            var diag = BuildEmptyTaskIdDiagnostic();
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        var cancelled = await _registry.CancelTaskAsync(task_id, cancellationToken).ConfigureAwait(false);

        if (!cancelled)
        {
            var diag = BuildCancelFailedDiagnostic(task_id);
            return ToolResultBuilder.Error().WithText(diag.FormattedMessage).WithDiagnostic(diag).Build();
        }

        return ToolResultBuilder.Success().WithText($"Task {task_id} cancelled").Build();
    }

    /// <summary>
    /// 强制杀死所有运行中的后台任务
    /// </summary>
    [McpTool(ShellToolNameConstants.ShellBackgroundKillAll, "Force kill ALL running background shell tasks and reclaim memory", "execution")]
    public async Task<ToolResult> ShellBackgroundKillAllAsync(
        CancellationToken cancellationToken = default)
    {
        var killedCount = await _registry.KillAllRunningAsync(cancellationToken).ConfigureAwait(false);

        return ToolResultBuilder.Success().WithText(killedCount > 0
            ? $"Killed {killedCount} running background task(s)"
            : "No running background tasks to kill").Build();
    }

    #region Private Methods

    private static string FormatStatus(TaskExecutionStatus status)
    {
        return status switch
        {
            TaskExecutionStatus.Pending => "Pending",
            TaskExecutionStatus.Running => "Running",
            TaskExecutionStatus.Completed => "Completed",
            TaskExecutionStatus.Failed => "Failed",
            TaskExecutionStatus.Cancelled => "Cancelled",
            _ => status.ToString()
        };
    }

    internal static ToolDiagnostic BuildEmptyTaskIdDiagnostic() =>
        ToolDiagnostic.Create(
            reason: "参数验证失败",
            formattedMessage: "task_id is required",
            details: [new DiagnosticDetail("field", "task_id")],
            suggestions: ["提供非空的 task_id 参数"]);

    internal static ToolDiagnostic BuildTaskNotFoundDiagnostic(string taskId) =>
        ToolDiagnostic.Create(
            reason: "任务未找到",
            formattedMessage: $"Task not found: {taskId}",
            details: [new DiagnosticDetail("task_id", taskId)],
            suggestions: ["使用 shell_background_list 查看所有后台任务"]);

    internal static ToolDiagnostic BuildCancelFailedDiagnostic(string taskId) =>
        ToolDiagnostic.Create(
            reason: "取消任务失败",
            formattedMessage: $"Cannot cancel task {taskId} — task may not exist or already completed",
            details: [new DiagnosticDetail("task_id", taskId)],
            suggestions: ["确认任务 ID 是否正确", "任务可能已完成或不存在"]);

    #endregion
}
