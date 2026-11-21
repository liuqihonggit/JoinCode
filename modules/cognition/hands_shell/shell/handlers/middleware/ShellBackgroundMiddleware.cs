namespace Tools.Shell;

/// <summary>
/// Shell 后台任务中间件 — 对齐 TS spawnShellTask/LocalShellTask
/// 当 background=true 时，先启动 SystemActuatorCommandContext，再立即转后台并注册到后台任务服务
/// 统一走 SystemActuatorCommandContext 路径，复用溢出文件机制，不再独立启动新进程
/// </summary>
[Register(typeof(IShellMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShellBackgroundMiddleware : ServiceEntity, IShellMiddleware {

    /// <summary>
    /// 构造后台任务中间件
    /// </summary>
    /// <param name="registry">系统执行器注册表</param>
    /// <param name="telemetryService">遥测服务（可选）</param>
    /// <param name="workflowConfig">工作流配置（可选 — 驱动 sandbox 门控）</param>
    /// <param name="sandboxManager">沙箱管理器（可选 — 提供运行期可用性）</param>
    public ShellBackgroundMiddleware(
        ISystemActuatorRegistry registry,
        ITelemetryService? telemetryService = null,
        IOptions<WorkflowConfig>? workflowConfig = null,
        ISandboxManager? sandboxManager = null) {
        _registry = registry;
        _telemetryService = telemetryService;
        _workflowConfig = workflowConfig;
        _sandboxManager = sandboxManager;
    }
    private readonly ISystemActuatorRegistry _registry;
    private readonly ITelemetryService? _telemetryService;
    private readonly IOptions<WorkflowConfig>? _workflowConfig;
    private readonly ISandboxManager? _sandboxManager;

    /// <inheritdoc />

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct) {
        if (context.Background != true) {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 对齐 TS spawnShellTask: 先启动进程，再立即转后台
        var cmdContext = await context.Provider.StartWithBackgroundSupportAsync(
            context.Command,
            context.Timeout,
            context.WorkingDirectory,
            shouldAutoBackground: false,
            disableSandbox: ShellSandboxPolicy.ResolveDisableSandbox(
                context.Command, context.DangerouslyDisableSandbox, _workflowConfig?.Value, _sandboxManager),
            cancellationToken: ct).ConfigureAwait(false);

        SystemActuatorBackgroundTaskInfo taskInfo;
        try {
            // 立即转后台 — 对齐 TS shellCommand.background(taskId)
            var taskId = cmdContext.TaskId;
            cmdContext.Background(taskId);

            // 注册到后台任务服务 — 输出通过 ISystemActuatorCommandContext.GetCurrentStdout() 获取
            // 所有权移交注册表：此处不得 Dispose，否则会取消进程等待并杀死仍在运行的后台进程
            taskInfo = await _registry.RegisterContextAsync(
                cmdContext, context.WorkingDirectory, ct).ConfigureAwait(false);
        } catch {
            await cmdContext.DisposeAsync().ConfigureAwait(false);
            throw;
        }

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