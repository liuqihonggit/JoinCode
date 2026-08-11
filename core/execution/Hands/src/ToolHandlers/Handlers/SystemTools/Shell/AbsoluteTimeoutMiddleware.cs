namespace Tools.Shell;

/// <summary>
/// 绝对超时中间件 — 读取 ShellPipelineContext.TimeoutPolicy 强制截断超时上限
/// 替代 FixedTimeoutMiddleware(120s) 硬编码，由类继承体系（OneShotCommandGroup/LongRunningGroup）驱动
/// 位置：管道最前端，在 Validation 之前
/// 超时后设置 IsError=true 的 ToolResult，触发 OnErrorToolInjectionMiddleware 注入续期工具
/// 配置: ShellExecutionConfig.AbsoluteTimeoutSeconds 覆盖默认120s，0=禁用
/// </summary>
[Register]
public sealed partial class AbsoluteTimeoutMiddleware : ServiceEntity, IShellMiddleware
{
    private readonly ShellExecutionConfig _config;
    private readonly ILogger<AbsoluteTimeoutMiddleware>? _logger;

    public AbsoluteTimeoutMiddleware(ShellExecutionConfig config, ILogger<AbsoluteTimeoutMiddleware>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <inheritdoc />
    public ErrorBehavior OnError => ErrorBehavior.Continue;

    /// <inheritdoc />
    public async Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        var policy = context.TimeoutPolicy;

        if (policy.AbsoluteTimeoutSeconds is not { } policySeconds || policySeconds <= 0)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        var configSeconds = _config.AbsoluteTimeoutSeconds;
        var effectiveSeconds = configSeconds > 0 ? configSeconds : policySeconds;

        var absoluteTimeout = TimeSpan.FromSeconds(effectiveSeconds);
        using var cts = TimeoutHelper.CreateLinkedTimeout(ct, absoluteTimeout);

        try
        {
            await next(context, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger?.LogWarning("Shell 命令绝对超时 ({Seconds}s): {Command}", effectiveSeconds, context.Command);
            SetTimeoutResult(context, effectiveSeconds);
        }
        catch (TimeoutException ex) when (!ct.IsCancellationRequested)
        {
            _logger?.LogWarning("Shell 命令超时: {Command} - {Message}", context.Command, ex.Message);
            SetTimeoutResult(context, effectiveSeconds);
        }
    }

    private static void SetTimeoutResult(ShellPipelineContext context, int seconds)
    {
        var toolName = context.Provider.Kind == SystemActuatorKind.PowerShell ? "PowerShell" : "Bash";
        var sb = new StringBuilder(512);
        sb.AppendLine($"命令执行超时（{seconds}秒）。");
        sb.AppendLine();
        sb.AppendLine($"**命令**: `{context.Command}`");
        sb.AppendLine();
        sb.AppendLine("如果需要继续执行此命令，请调用 `resume_timed_out_task` 工具：");
        sb.AppendLine($"- original_command: \"{context.Command}\"");
        sb.AppendLine($"- original_tool: \"{toolName}\"");
        sb.AppendLine("- timeout_minutes: 10 (默认10分钟续期)");

        var diagnostic = BuildTimeoutDiagnostic(sb.ToString(), context.Command, seconds, toolName);
        context.Result = ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
    }

    internal static ToolDiagnostic BuildTimeoutDiagnostic(string formattedMessage, string command, int seconds, string toolName) =>
        ToolDiagnostic.Create(
            reason: "命令执行超时",
            formattedMessage: formattedMessage,
            details:
            [
                new DiagnosticDetail("command", command),
                new DiagnosticDetail("timeout_seconds", seconds.ToString()),
                new DiagnosticDetail("tool", toolName)
            ],
            suggestions: ["调用 resume_timed_out_task 工具续期执行"]);
}
