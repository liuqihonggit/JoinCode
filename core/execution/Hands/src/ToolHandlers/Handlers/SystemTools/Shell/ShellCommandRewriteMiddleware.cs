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
#pragma warning disable JCC1001 // context 字典仅运行时传参，不参与 AOT 序列化
            var rewriteContext = new Dictionary<string, object>
            {
                ["ShellKind"] = context.Provider.Kind
            };
#pragma warning restore JCC1001
            var result = _rewriterRegistry.Rewrite(context.Command, rewriteContext);
            if (result.WasRewritten)
            {
                _logger?.LogWarning(
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
