namespace Tools.Shell;

/// <summary>
/// Shell 超时关键字中间件 — 检测脚本内 wait/sleep/Start-Sleep/timeout 等关键字
/// 自动提取等待时间，据此调整超时上限，规避默认超时终止
/// 冲突处理: 用户显式传入 timeout 不足时，直接返回 Error 给 AI（不抛异常给软件）
/// 位置: ShellSearchTimeoutMiddleware 之后、AbsoluteTimeoutMiddleware 之前
/// </summary>
[Register(typeof(IShellMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShellTimeoutKeywordMiddleware : ServiceEntity, IShellMiddleware
{
    private readonly ShellExecutionConfig _config;
    private readonly ILogger<ShellTimeoutKeywordMiddleware>? _logger;

    public ShellTimeoutKeywordMiddleware(ShellExecutionConfig config, ILogger<ShellTimeoutKeywordMiddleware>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
    }

    /// <inheritdoc />
    public Task InvokeAsync(ShellPipelineContext context, MiddlewareDelegate<ShellPipelineContext> next, CancellationToken ct)
    {
        var maxWaitSeconds = ShellTimeoutKeywordExtractor.ExtractMaxWaitSeconds(context.Command);
        if (maxWaitSeconds is not { } waitSeconds)
            return next(context, ct);

        var requiredSeconds = waitSeconds + _config.TimeoutKeywordBufferSeconds;
        var requiredMs = requiredSeconds * 1000;

        var effectiveMs = context.OverrideTimeout ?? context.Timeout ?? (_config.DefaultTimeoutSeconds * 1000);

        if (requiredMs <= effectiveMs)
            return next(context, ct);

        if (context.Timeout is { } userTimeoutMs)
        {
            _logger?.LogWarning(
                "脚本超时关键字冲突: 命令含 {Wait}s 等待，用户传入 timeout {User}s 不足，需要至少 {Required}s",
                waitSeconds, userTimeoutMs / 1000, requiredSeconds);

            var userSeconds = userTimeoutMs / 1000;
            var diagnostic = BuildConflictDiagnostic(context.Command, waitSeconds, userSeconds, requiredSeconds);
            context.Result = ToolResultBuilder.Error().WithText(diagnostic.FormattedMessage).WithDiagnostic(diagnostic).Build();
            return Task.CompletedTask;
        }

        _logger?.LogDebug(
            "脚本超时关键字自动延长: 命令含 {Wait}s 等待，超时从 {Effective}s 延长到 {Required}s",
            waitSeconds, effectiveMs / 1000, requiredSeconds);

        context.OverrideTimeout = requiredMs;
        return next(context, ct);
    }

    internal static ToolDiagnostic BuildConflictDiagnostic(string command, int waitSeconds, int userSeconds, int requiredSeconds)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine($"命令内含 {waitSeconds} 秒等待，但传入超时 {userSeconds} 秒不足。");
        sb.AppendLine();
        sb.AppendLine($"**命令**: `{command}`");
        sb.AppendLine();
        sb.AppendLine($"请增大 timeout 参数到至少 {requiredSeconds} 秒（等待时间 {waitSeconds}s + 缓冲时间），或移除脚本内的等待关键字。");

        return ToolDiagnostic.Create(
            reason: "脚本超时关键字冲突",
            formattedMessage: sb.ToString(),
            details:
            [
                new DiagnosticDetail("command", command),
                new DiagnosticDetail("wait_seconds", waitSeconds.ToString()),
                new DiagnosticDetail("user_timeout_seconds", userSeconds.ToString()),
                new DiagnosticDetail("required_timeout_seconds", requiredSeconds.ToString())
            ],
            suggestions: [$"将 timeout 参数增大到至少 {requiredSeconds} 秒"]);
    }
}
