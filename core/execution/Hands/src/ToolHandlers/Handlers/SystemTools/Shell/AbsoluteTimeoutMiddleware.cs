namespace Tools.Shell;

/// <summary>
/// 绝对超时中间件 — 读取 ShellPipelineContext.TimeoutPolicy 强制截断超时上限
/// 替代 FixedTimeoutMiddleware(120s) 硬编码，由类继承体系（OneShotCommandGroup/LongRunningGroup）驱动
/// 位置：管道最前端，在 Validation 之前
/// 超时后设置 IsError=true 的 ToolResult，触发 OnErrorToolInjectionMiddleware 注入续期工具
/// </summary>
[Register]
public sealed partial class AbsoluteTimeoutMiddleware : ServiceEntity, IShellMiddleware
{
    private readonly ILogger<AbsoluteTimeoutMiddleware>? _logger;

    public AbsoluteTimeoutMiddleware(ILogger<AbsoluteTimeoutMiddleware>? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        var policy = context.TimeoutPolicy;

        if (policy.AbsoluteTimeoutSeconds is not { } absoluteSeconds || absoluteSeconds <= 0)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var absoluteTimeout = TimeSpan.FromSeconds(absoluteSeconds);
        using var cts = TimeoutHelper.CreateLinkedTimeout(ct, absoluteTimeout);

        try
        {
            await next(context, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger?.LogWarning("Shell 命令绝对超时 ({Seconds}s): {Command}", absoluteSeconds, context.Command);

            var toolName = context.Provider.Kind == SystemActuatorKind.PowerShell ? "PowerShell" : "Bash";
            var sb = new StringBuilder(512);
            sb.AppendLine($"命令执行超时（{absoluteSeconds}秒）。");
            sb.AppendLine();
            sb.AppendLine($"**命令**: `{context.Command}`");
            sb.AppendLine();
            sb.AppendLine("如果需要继续执行此命令，请调用 `resume_timed_out_task` 工具：");
            sb.AppendLine($"- original_command: \"{context.Command}\"");
            sb.AppendLine($"- original_tool: \"{toolName}\"");
            sb.AppendLine("- timeout_minutes: 10 (默认10分钟续期)");

            context.Result = ToolResultBuilder.Error().WithText(sb.ToString()).Build();
        }
        catch (TimeoutException ex) when (!ct.IsCancellationRequested)
        {
            _logger?.LogWarning("Shell 命令超时: {Command} - {Message}", context.Command, ex.Message);

            var toolName = context.Provider.Kind == SystemActuatorKind.PowerShell ? "PowerShell" : "Bash";
            var sb = new StringBuilder(512);
            sb.AppendLine($"命令执行超时（{absoluteSeconds}秒）。");
            sb.AppendLine();
            sb.AppendLine($"**命令**: `{context.Command}`");
            sb.AppendLine();
            sb.AppendLine("如果需要继续执行此命令，请调用 `resume_timed_out_task` 工具：");
            sb.AppendLine($"- original_command: \"{context.Command}\"");
            sb.AppendLine($"- original_tool: \"{toolName}\"");
            sb.AppendLine("- timeout_minutes: 10 (默认10分钟续期)");

            context.Result = ToolResultBuilder.Error().WithText(sb.ToString()).Build();
        }
    }
}
