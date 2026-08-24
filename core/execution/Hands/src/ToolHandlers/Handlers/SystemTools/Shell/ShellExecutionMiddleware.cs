namespace Tools.Shell;

/// <summary>
/// Shell 命令执行中间件 — 核心执行逻辑
/// 启动命令进程、注册后台化事件、注册前台任务、等待结果
/// </summary>
[Register(typeof(IShellMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShellExecutionMiddleware : ServiceEntity, IShellMiddleware
{

    public ShellExecutionMiddleware(ISystemActuatorRegistry registry, IForegroundTaskRegistry? foregroundTaskRegistry = null, ILogger<ShellExecutionMiddleware>? logger = null)
    {
        _registry = registry;
        _foregroundTaskRegistry = foregroundTaskRegistry;
        _logger = logger;
    }
    private readonly ISystemActuatorRegistry _registry;
    private readonly IForegroundTaskRegistry? _foregroundTaskRegistry;
    private readonly ILogger<ShellExecutionMiddleware>? _logger;

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        var shouldAutoBackground = context.AutoBackground != false
            && SystemActuatorBackgroundConstants.IsAutoBackgroundAllowed(context.Command);

        await using var cmdContext = await context.Provider.StartWithBackgroundSupportAsync(
            context.Command,
            context.OverrideTimeout ?? context.Timeout,
            context.WorkingDirectory,
            shouldAutoBackground: shouldAutoBackground,
            disableSandbox: context.DangerouslyDisableSandbox == true,
            cancellationToken: ct).ConfigureAwait(false);

        if (cmdContext is SystemActuatorCommandContext actuatorCtx)
        {
            actuatorCtx.Backgrounded += (ctx, taskId) =>
            {
                _ = _registry.RegisterContextAsync(ctx, context.WorkingDirectory, cancellationToken: default);
            };
        }

        _foregroundTaskRegistry?.Register(cmdContext);

        var progressType = context.Provider.Kind == SystemActuatorKind.PowerShell ? "ps_progress" : "bash_progress";
        using var progressTimer = context.OnProgress is not null
            ? CreateProgressTimer(cmdContext, context.OnProgress, progressType, _logger)
            : null;

        var result = await cmdContext.ResultTask.ConfigureAwait(false);

        _foregroundTaskRegistry?.Unregister(cmdContext.TaskId);

        context.ExecutionResult = result;

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 创建进度报告定时器
    /// </summary>
    private static Timer CreateProgressTimer(ISystemActuatorCommandContext context, ToolProgressCallback onProgress, string progressType, ILogger<ShellExecutionMiddleware>? logger = null)
    {
        var startTime = Environment.TickCount64;
        var progressCounter = 0;

        return new Timer(_ =>
        {
            try
            {
                if (context.Status != SystemActuatorCommandStatus.Running) return;

                var elapsedMs = Environment.TickCount64 - startTime;
                var currentOutput = context.GetCurrentStdout();
                var totalLines = currentOutput.Count(c => c == '\n') + 1;
                var totalBytes = Encoding.UTF8.GetByteCount(currentOutput);

                var lastLines = GetLastNLines(currentOutput, 5);
                var fullOutput = GetLastNLines(currentOutput, 100);

                onProgress(new ToolProgressData
                {
                    ProgressType = progressType,
                    ToolUseId = $"{progressType}-{progressCounter++}",
                    Message = lastLines,
                    ElapsedTimeMs = elapsedMs,
                    Extra = new Dictionary<string, JsonElement>
                    {
                        ["output"] = JsonSerializer.SerializeToElement(lastLines, ToolsJsonContext.Default.String),
                        ["fullOutput"] = JsonSerializer.SerializeToElement(fullOutput, ToolsJsonContext.Default.String),
                        ["totalLines"] = JsonSerializer.SerializeToElement(totalLines, ToolsJsonContext.Default.Int32),
                        ["totalBytes"] = JsonSerializer.SerializeToElement(totalBytes, ToolsJsonContext.Default.Int64),
                        ["taskId"] = JsonSerializer.SerializeToElement(context.TaskId, ToolsJsonContext.Default.String),
                    }
                });
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "进度报告发送失败");
            }
        }, null, TimeSpan.FromMilliseconds(SystemActuatorBackgroundConstants.ProgressThresholdMs), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 获取字符串的最后 N 行
    /// </summary>
    private static string GetLastNLines(string text, int lineCount)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var lines = text.Split('\n');
        if (lines.Length <= lineCount) return text.TrimEnd();

        var lastLines = lines[^lineCount..];
        return string.Join('\n', lastLines).TrimEnd();
    }
}
