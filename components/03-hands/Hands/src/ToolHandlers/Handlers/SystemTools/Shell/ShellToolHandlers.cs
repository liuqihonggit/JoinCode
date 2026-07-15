namespace Tools.Handlers;

/// <summary>
/// Shell 执行工具处理器 - 提供 CMD 和 PowerShell 命令执行功能
/// 通过中间件管道处理验证、分类、sed拦截、后台判断、执行、输出格式化
/// 继承 ShellToolBase 获得 PowerShell 门控、进程看护、压缩标记
/// </summary>
[McpToolHandler(ToolCategory.Shell)]
public partial class ShellToolHandlers : ShellToolBase
{
    private readonly MiddlewarePipeline<ShellContext> _pipeline;
    private readonly IShellBackgroundTaskService? _backgroundTaskService;

    public override string ToolName => ShellToolNameConstants.Bash;

    public ShellToolHandlers(
        MiddlewarePipeline<ShellContext> pipeline,
        IShellToolGateService? gateService = null,
        IShellProcessWatchdog? watchdog = null,
        IShellBackgroundTaskService? backgroundTaskService = null)
        : base(gateService, watchdog)
    {
        _pipeline = pipeline ?? throw new ArgumentNullException(nameof(pipeline));
        _backgroundTaskService = backgroundTaskService;
    }

    /// <summary>
    /// 执行 CMD 命令
    /// 与 TS BashTool 对齐：超时自动后台化、assistant 自动后台化、description 参数
    /// </summary>
    [McpTool(ShellToolNameConstants.Bash, "Execute a Windows CMD command. The description parameter briefly describes the command purpose", "execution")]
    public async Task<ToolResult> ShellExecuteAsync(
        [McpToolParameter("CMD command to execute")] string command,
        [McpToolParameter("Brief description of the command purpose", Required = false)] string? description = null,
        [McpToolParameter("Timeout in milliseconds, default 120000ms", Required = false, DefaultValue = "120000")] int? timeout = null,
        [McpToolParameter("Working directory, defaults to current directory", Required = false)] string? working_directory = null,
        [McpToolParameter("Run in background (do not wait for completion)", Required = false, DefaultValue = "false")] bool? background = null,
        [McpToolParameter("Enable auto-backgrounding on timeout — 对齐 TS shouldAutoBackground", Required = false, DefaultValue = "true")] bool? auto_background = null,
        [McpToolParameter("Override sandbox mode for this command — 对齐 TS dangerouslyDisableSandbox", Required = false, DefaultValue = "false")] bool? dangerously_disable_sandbox = null,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        var context = new ShellContext
        {
            Command = command,
            IsPowerShell = false,
            Description = description,
            Timeout = timeout,
            WorkingDirectory = working_directory,
            Background = background,
            AutoBackground = auto_background,
            DangerouslyDisableSandbox = dangerously_disable_sandbox,
            CancellationToken = cancellationToken,
            OnProgress = onProgress,
        };

        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.Result ?? ResultBuilder.Error().WithText("Pipeline did not produce a result").Build();
    }

    /// <summary>
    /// 执行 PowerShell 命令
    /// 与 TS BashTool 对齐：添加 description 参数和退出码语义解释
    /// </summary>
    [McpTool(ShellToolNameConstants.Powershell, "Execute a PowerShell command. The description parameter briefly describes the command purpose", "execution")]
    public async Task<ToolResult> PowerShellExecuteAsync(
        [McpToolParameter("PowerShell command to execute")] string command,
        [McpToolParameter("Brief description of the command purpose", Required = false)] string? description = null,
        [McpToolParameter("Timeout in milliseconds, default 120000ms", Required = false, DefaultValue = "120000")] int? timeout = null,
        [McpToolParameter("Working directory, defaults to current directory", Required = false)] string? working_directory = null,
        [McpToolParameter("Run in background (do not wait for completion)", Required = false, DefaultValue = "false")] bool? background = null,
        [McpToolParameter("Enable auto-backgrounding on timeout — 对齐 TS shouldAutoBackground", Required = false, DefaultValue = "true")] bool? auto_background = null,
        [McpToolParameter("Override sandbox mode for this command — 对齐 TS dangerouslyDisableSandbox", Required = false, DefaultValue = "false")] bool? dangerously_disable_sandbox = null,
        CancellationToken cancellationToken = default,
        ToolProgressCallback? onProgress = null)
    {
        var gateResult = CheckGate(isPowerShellCall: true);
        if (gateResult is not null) return gateResult;
        var context = new ShellContext
        {
            Command = command,
            IsPowerShell = true,
            Description = description,
            Timeout = timeout,
            WorkingDirectory = working_directory,
            Background = background,
            AutoBackground = auto_background,
            DangerouslyDisableSandbox = dangerously_disable_sandbox,
            CancellationToken = cancellationToken,
            OnProgress = onProgress,
        };

        await _pipeline.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);

        return context.Result ?? ResultBuilder.Error().WithText("Pipeline did not produce a result").Build();
    }

    /// <summary>
    /// 获取后台任务状态
    /// </summary>
    [McpTool(ShellToolNameConstants.ShellBackgroundGet, "Get background shell task status", "execution")]
    public async Task<ToolResult> ShellBackgroundGetAsync(
        [McpToolParameter("Task ID")] string task_id,
        CancellationToken cancellationToken = default)
    {
        if (_backgroundTaskService == null)
        {
            return ResultBuilder.Error().WithText("Background task service is not available").Build();
        }

        if (string.IsNullOrWhiteSpace(task_id))
        {
            return ResultBuilder.Error().WithText("task_id is required").Build();
        }

        var task = await _backgroundTaskService.GetTaskAsync(task_id, cancellationToken).ConfigureAwait(false);

        if (task == null)
        {
            return ResultBuilder.Error().WithText($"Task not found: {task_id}").Build();
        }

        var response = new StringBuilder();
        response.AppendLine("Background task status");
        response.AppendLine();
        response.AppendLine($"Task ID: {task.TaskId}");
        response.AppendLine($"Command: {task.Command}");
        response.AppendLine($"Status: {FormatStatus(task.Status)}");
        response.AppendLine($"Created: {task.CreatedAt:yyyy-MM-dd HH:mm:ss}");

        if (task.StartedAt.HasValue)
        {
            response.AppendLine($"Started: {task.StartedAt.Value:yyyy-MM-dd HH:mm:ss}");
        }

        if (task.CompletedAt.HasValue)
        {
            response.AppendLine($"Completed: {task.CompletedAt.Value:yyyy-MM-dd HH:mm:ss}");
        }

        if (task.ExitCode.HasValue)
        {
            response.AppendLine($"Exit code: {task.ExitCode}");
        }

        if (!string.IsNullOrEmpty(task.ErrorMessage))
        {
            response.AppendLine($"Error: {task.ErrorMessage}");
        }

        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 列出所有后台任务
    /// </summary>
    [McpTool(ShellToolNameConstants.ShellBackgroundList, "List all background shell tasks", "execution")]
    public async Task<ToolResult> ShellBackgroundListAsync(
        CancellationToken cancellationToken = default)
    {
        if (_backgroundTaskService == null)
        {
            return ResultBuilder.Error().WithText("Background task service is not available").Build();
        }

        var tasks = await _backgroundTaskService.ListTasksAsync(cancellationToken).ConfigureAwait(false);

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
                var statusIcon = task.Status switch
                {
                    TaskExecutionStatus.Running => StatusSymbol.Refresh.ToValue(),
                    TaskExecutionStatus.Completed => StatusSymbol.Tick.ToValue(),
                    TaskExecutionStatus.Failed => StatusSymbol.Cross.ToValue(),
                    TaskExecutionStatus.Cancelled => StatusSymbol.Prohibited.ToValue(),
                    _ => StatusSymbol.Circle.ToValue()
                };

                response.AppendLine($"{statusIcon} [{task.TaskId}] {task.Command[..Math.Min(40, task.Command.Length)]}...");
                response.AppendLine($"   Status: {FormatStatus(task.Status)} | Created: {task.CreatedAt:MM-dd HH:mm}");
            }
        }

        return ResultBuilder.Success().WithText(response.ToString()).Build();
    }

    /// <summary>
    /// 获取后台任务输出
    /// </summary>
    [McpTool(ShellToolNameConstants.ShellBackgroundOutput, "Get output of a background shell task", "execution")]
    public async Task<ToolResult> ShellBackgroundOutputAsync(
        [McpToolParameter("Task ID")] string task_id,
        CancellationToken cancellationToken = default)
    {
        if (_backgroundTaskService == null)
        {
            return ResultBuilder.Error().WithText("Background task service is not available").Build();
        }

        if (string.IsNullOrWhiteSpace(task_id))
        {
            return ResultBuilder.Error().WithText("task_id is required").Build();
        }

        var output = await _backgroundTaskService.GetTaskOutputAsync(task_id, cancellationToken).ConfigureAwait(false);

        if (string.IsNullOrEmpty(output))
        {
            return ResultBuilder.Success().WithText("(No output yet)").Build();
        }

        return ResultBuilder.Success().WithText(output).Build();
    }

    /// <summary>
    /// 取消后台任务
    /// </summary>
    [McpTool(ShellToolNameConstants.ShellBackgroundCancel, "Cancel a background shell task", "execution")]
    public async Task<ToolResult> ShellBackgroundCancelAsync(
        [McpToolParameter("Task ID")] string task_id,
        CancellationToken cancellationToken = default)
    {
        if (_backgroundTaskService == null)
        {
            return ResultBuilder.Error().WithText("Background task service is not available").Build();
        }

        if (string.IsNullOrWhiteSpace(task_id))
        {
            return ResultBuilder.Error().WithText("task_id is required").Build();
        }

        var cancelled = await _backgroundTaskService.CancelTaskAsync(task_id, cancellationToken).ConfigureAwait(false);

        if (!cancelled)
        {
            return ResultBuilder.Error().WithText($"Cannot cancel task {task_id} — task may not exist or already completed").Build();
        }

        return ResultBuilder.Success().WithText($"Task {task_id} cancelled").Build();
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

    #endregion
}
