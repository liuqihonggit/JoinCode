namespace Tools.Shell;

/// <summary>
/// Shell 后台任务中间件 — 处理显式后台运行请求
/// 当 background=true 时，创建后台任务并返回任务信息
/// </summary>
[Register]
public sealed partial class ShellBackgroundMiddleware : IShellMiddleware
{
    [Inject] private readonly IShellBackgroundTaskService? _backgroundTaskService;
    [Inject] private readonly ITelemetryService? _telemetryService;

    /// <inheritdoc />
    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <inheritdoc />
    public async Task InvokeAsync(ShellContext context, MiddlewareDelegate<ShellContext> next, CancellationToken ct)
    {
        if (context.Background == true && _backgroundTaskService != null)
        {
            var taskInfo = await _backgroundTaskService.CreateTaskAsync(
                context.Command,
                context.WorkingDirectory,
                ct).ConfigureAwait(false);

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
            return; // 短路
        }

        await next(context, ct).ConfigureAwait(false);
    }

    private void RecordShellMetrics(string shellType, string operation, string result)
        => _telemetryService?.RecordCount("shell.execution.count", new Dictionary<string, string> { ["shell"] = shellType, ["operation"] = operation, ["result"] = result }, description: "Shell execution count");
}
