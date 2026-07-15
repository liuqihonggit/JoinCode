namespace Tools.Shell;

/// <summary>
/// Shell 命令执行中间件 — 核心执行逻辑
/// 启动命令进程、注册后台化事件、注册前台任务、等待结果
/// </summary>
[Register]
public sealed partial class ShellExecutionMiddleware : IShellMiddleware
{
    [Inject] private readonly IShellExecutionService _shellExecutionService;
    [Inject] private readonly IShellBackgroundTaskService? _backgroundTaskService;
    [Inject] private readonly IForegroundTaskRegistry? _foregroundTaskRegistry;

    /// <inheritdoc />

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

    /// <inheritdoc />
    public async Task InvokeAsync(ShellContext context, MiddlewareDelegate<ShellContext> next, CancellationToken ct)
    {
        // 判断是否允许自动后台化 — 对齐 TS isAutobackgroundingAllowed
        var shouldAutoBackground = context.AutoBackground != false
            && ShellBackgroundConstants.IsAutoBackgroundAllowed(context.Command);

        // 使用可后台化的执行上下文 — 对齐 TS ShellCommand.exec
        using var cmdContext = await _shellExecutionService.StartWithBackgroundSupportAsync(
            context.Command,
            context.Timeout,
            context.WorkingDirectory,
            isPowerShell: context.IsPowerShell,
            shouldAutoBackground: shouldAutoBackground,
            disableSandbox: context.DangerouslyDisableSandbox == true,
            cancellationToken: ct).ConfigureAwait(false);

        // 注册后台化事件 — 将后台化的命令注册到后台任务服务
        if (_backgroundTaskService != null && cmdContext is ShellCommandContext shellCtx)
        {
            shellCtx.Backgrounded += (ctx, taskId) =>
            {
                // 超时/assistant 自动后台化时，将进程注册到后台任务服务
                _ = _backgroundTaskService.CreateTaskAsync(
                    ctx.Command,
                    context.WorkingDirectory,
                    cancellationToken: default);
            };
        }

        // 注册前台任务 — 对齐 TS registerForeground，支持 Ctrl+B 后台化
        _foregroundTaskRegistry?.Register(cmdContext);

        // 对齐 TS bash_progress: 定时轮询输出并报告进度
        var progressType = context.IsPowerShell ? "ps_progress" : "bash_progress";
        using var progressTimer = context.OnProgress is not null
            ? CreateProgressTimer(cmdContext, context.OnProgress, progressType)
            : null;

        // 等待命令完成或后台化
        var result = await cmdContext.ResultTask.ConfigureAwait(false);

        // 注销前台任务
        _foregroundTaskRegistry?.Unregister(cmdContext.TaskId);

        context.ExecutionResult = result;

        await next(context, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 创建进度报告定时器 — 对齐 TS TaskOutput 共享轮询器（1s interval）
    /// 定时轮询 ShellCommandContext 的当前输出，通过 onProgress 回调报告进度
    /// </summary>
    private static Timer CreateProgressTimer(IShellCommandContext context, ToolProgressCallback onProgress, string progressType)
    {
        var startTime = Environment.TickCount64;
        var progressCounter = 0;

        return new Timer(_ =>
        {
            try
            {
                if (context.Status != ShellCommandStatus.Running) return;

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
                // 进度报告失败不影响命令执行
                System.Diagnostics.Trace.WriteLine($"进度报告发送失败: {ex.Message}");
            }
        }, null, TimeSpan.FromMilliseconds(ShellBackgroundConstants.ProgressThresholdMs), TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// 获取字符串的最后 N 行 — 对齐 TS tailFile
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
