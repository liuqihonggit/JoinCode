namespace Tools.Shell;

/// <summary>
/// Shell 后台任务中间件 — 对齐 TS spawnShellTask/LocalShellTask
/// 当 background=true 时，先启动 ShellCommandContext，再立即转后台并注册到后台任务服务
/// 统一走 ShellCommandContext 路径，复用溢出文件机制，不再独立启动新进程
/// </summary>
[Register]
public sealed partial class ShellBackgroundMiddleware : IShellMiddleware
{
    [Inject] private readonly IShellExecutionService _shellExecutionService;
    [Inject] private readonly IShellBackgroundTaskService? _backgroundTaskService;
    [Inject] private readonly ITelemetryService? _telemetryService;

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        if (context.Background != true || _backgroundTaskService == null)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 对齐 TS spawnShellTask: 先启动进程，再立即转后台
        await using var cmdContext = await _shellExecutionService.StartWithBackgroundSupportAsync(
            context.Command,
            context.Timeout,
            context.WorkingDirectory,
            isPowerShell: context.IsPowerShell,
            shouldAutoBackground: false,
            disableSandbox: context.DangerouslyDisableSandbox == true,
            cancellationToken: ct).ConfigureAwait(false);

        // 立即转后台 — 对齐 TS shellCommand.background(taskId)
        var taskId = cmdContext.TaskId;
        if (cmdContext is ShellCommandContext shellCtx)
        {
            shellCtx.Background(taskId);
        }

        // 注册到后台任务服务 — 输出通过 ShellCommandContext.GetCurrentStdout() 获取
        var taskInfo = await _backgroundTaskService.RegisterContextAsync(
            cmdContext, context.WorkingDirectory, ct).ConfigureAwait(false);

        var shellType = context.IsPowerShell ? "powershell" : "cmd";
        RecordShellMetrics(shellType, "background", "ok");

        var response = new StringBuilder();
        response.AppendLine("Background task created");
        response.AppendLine($"Task ID: {taskInfo.TaskId}");
        response.AppendLine($"Command: {taskInfo.Command}");
        response.AppendLine();
        response.AppendLine("Use these commands to check task status:");
        response.AppendLine($"  - Get status: shell_background_get task_id=\"{taskInfo.TaskId}\"");
        response.AppendLine($"  - Get output: shell_background_output task_id=\"{taskInfo.TaskId}\"");
        response.AppendLine($"  - Cancel task: shell_background_cancel task_id=\"{taskInfo.TaskId}\"");

        context.BackgroundResult = ResultBuilder.Success().WithText(response.ToString()).Build();
        context.Result = context.BackgroundResult;
    }

    private void RecordShellMetrics(string shellType, string operation, string result)
        => _telemetryService?.RecordCount("shell.execution.count", new Dictionary<string, string> { ["shell"] = shellType, ["operation"] = operation, ["result"] = result }, description: "Shell execution count");
}
