namespace Tools.Shell;

/// <summary>
/// Shell 扰动审计中间件 — 执行后记录 MTP 扰动统计特征（PostToolUse 只审计不清理）。
/// <para>
/// MTP 扰动纵深防御约束第6条：PostToolUse 只审计不清理（清理本身也是 bash 操作，补救不能代替预防）。
/// 集成位置：ShellExecutionMiddleware 之后，使用 post-next 模式（先让管道完成，再审计）。
/// </para>
/// </summary>
[Register(typeof(IShellMiddleware), ServiceLifetime.Singleton)]
public sealed partial class ShellPerturbationAuditMiddleware : ServiceEntity, IShellMiddleware
{
    private readonly MtpPerturbationNode _perturbationNode;
    private readonly ILogger<ShellPerturbationAuditMiddleware>? _logger;

    /// <summary>
    /// 构造 Shell 扰动审计中间件
    /// </summary>
    /// <param name="perturbationNode">MTP 扰动检测 node</param>
    /// <param name="logger">日志器（可选）</param>
    public ShellPerturbationAuditMiddleware(
        MtpPerturbationNode perturbationNode,
        ILogger<ShellPerturbationAuditMiddleware>? logger = null)
    {
        _perturbationNode = perturbationNode ?? throw new ArgumentNullException(nameof(perturbationNode));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(
        ShellPipelineContext context,
        MiddlewareDelegate<ShellPipelineContext> next,
        CancellationToken ct)
    {
        await next(context, ct).ConfigureAwait(false);

        if (context.ExecutionResult is null)
            return;

        var report = _perturbationNode.Record(
            context.Command,
            context.ExecutionResult.ExitCode ?? 0,
            context.ExecutionResult.Stderr);

        if (report.ShouldTriggerAdaptive)
        {
            _logger?.LogWarning(
                "MTP 扰动自适应触发 — 连续 {Count} 次异常，建议启用 AntiCharLossConfirm 模式。最近命令: {Command}",
                report.ConsecutiveAnomalies,
                report.LastCommand);
        }
    }
}
