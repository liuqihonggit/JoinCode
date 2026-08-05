namespace Tools.Shell;

/// <summary>
/// Shell 后台任务中间件 — 对齐 TS spawnShellTask/LocalShellTask
/// 当 background=true 时，先启动 SystemActuatorCommandContext，再立即转后台并注册到后台任务服务
/// 统一走 SystemActuatorCommandContext 路径，复用溢出文件机制，不再独立启动新进程
/// </summary>
[Register]
public sealed partial class ShellBackgroundMiddleware : ServiceEntity, IShellMiddleware
{

    public ShellBackgroundMiddleware(ISystemActuatorRegistry registry, ITelemetryService? telemetryService = null)
    {
        _registry = registry;
        _telemetryService = telemetryService;
    }
    [Inject] private readonly ISystemActuatorRegistry _registry;
    [Inject] private readonly ITelemetryService? _telemetryService;

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        if (context.Background != true)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 对齐 TS spawnShellTask: 先启动进程，再立即转后台
        await using var cmdContext = await context.Provider.StartWithBackgroundSupportAsync(
            context.Command,
            context.Timeout,
            context.WorkingDirectory,
            shouldAutoBackground: false,
            disableSandbox: context.DangerouslyDisableSandbox == true,
            cancellationToken: ct).ConfigureAwait(false);

        // 立即转后台 — 对齐 TS shellCommand.background(taskId)
        var taskId = cmdContext.TaskId;
        cmdContext.Background(taskId);

        // 注册到后台任务服务 — 输出通过 ISystemActuatorCommandContext.GetCurrentStdout() 获取
        var taskInfo = await _registry.RegisterContextAsync(
            cmdContext, context.WorkingDirectory, ct).ConfigureAwait(false);

        var shellType = context.Provider.Kind.Id;
        ToolTelemetryHelper.RecordToolCount(_telemetryService, "shell.execution.count", new Dictionary<string, string> { ["shell"] = shellType, ["operation"] = "background", ["result"] = "ok" });

        var response = new StringBuilder();
        response.AppendLine("Background task created");
        response.AppendLine($"Task ID: {taskInfo.TaskId}");
        response.AppendLine($"Command: {taskInfo.Command}");
        response.AppendLine();
        response.AppendLine("Use these commands to check task status:");
        response.AppendLine($"  - Get status: shell_background_get task_id=\"{taskInfo.TaskId}\"");
        response.AppendLine($"  - Get output: shell_background_output task_id=\"{taskInfo.TaskId}\"");
        response.AppendLine($"  - Cancel task: shell_background_cancel task_id=\"{taskInfo.TaskId}\"");

        context.BackgroundResult = ToolResultBuilder.Success().WithText(response.ToString()).Build();
        context.Result = context.BackgroundResult;
    }

}
