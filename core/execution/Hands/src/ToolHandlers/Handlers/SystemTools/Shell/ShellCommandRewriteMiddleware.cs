namespace Tools.Shell;

/// <summary>
/// Shell 命令改写中间件 — 在命令执行前调用 CommandRewriterRegistry 进行改写
/// <para>
/// 集成位置：ShellValidationMiddleware 之后、ShellPathGateMiddleware 之前
/// 改写器示例：GhPrBodyRewriter（为 gh pr create 添加 --body）、VpnRouteRewriter（VPN 代理切换）
/// </para>
/// </summary>
[Register]
public sealed partial class ShellCommandRewriteMiddleware : ServiceEntity, IShellMiddleware
{
    [Inject] private readonly Core.Hooks.Execution.CommandRewriterRegistry _rewriterRegistry;
    [Inject] private readonly ILogger<ShellCommandRewriteMiddleware>? _logger;

    public ShellCommandRewriteMiddleware(
        Core.Hooks.Execution.CommandRewriterRegistry rewriterRegistry,
        ILogger<ShellCommandRewriteMiddleware>? logger = null)
    {
        _rewriterRegistry = rewriterRegistry ?? throw new ArgumentNullException(nameof(rewriterRegistry));
        _logger = logger;
    }

    /// <inheritdoc />
    public Task InvokeAsync(
        ShellPipelineContext context,
        MiddlewareDelegate<ShellPipelineContext> next,
        CancellationToken ct)
    {
        // 改写命令
        if (!string.IsNullOrEmpty(context.Command))
        {
            var result = _rewriterRegistry.Rewrite(context.Command);
            if (result.WasRewritten)
            {
                _logger?.LogInformation(
                    "Shell 命令已改写: {Original} → {Rewritten} (改写器: {Rewriter})",
                    result.OriginalCommand,
                    result.RewrittenCommand,
                    result.RewriterName);

                context.Command = result.RewrittenCommand;
            }
        }

        return next(context, ct);
    }
}
