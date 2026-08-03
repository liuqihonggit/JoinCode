
namespace Core.Permission;

/// <summary>
/// 分类器中间件 — Auto/Default 模式下使用工具过滤器和分类器判断权限
/// Default 模式描述为"根据配置自动判断是否需要确认"，因此也使用分类器自动批准只读/安全写入工具
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class AutoClassifierMiddleware : IPermissionMiddleware
{
    private readonly IToolPermissionFilter? _toolPermissionFilter;
    private readonly IAutoModeClassifier? _autoModeClassifier;

    /// <inheritdoc />

    /// <inheritdoc />

    /// <summary>
    /// 创建 AutoClassifierMiddleware
    /// </summary>
    public AutoClassifierMiddleware(
        IToolPermissionFilter? toolPermissionFilter = null,
        IAutoModeClassifier? autoModeClassifier = null)
    {
        _toolPermissionFilter = toolPermissionFilter;
        _autoModeClassifier = autoModeClassifier;
    }

    /// <inheritdoc />
    public async Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        // Auto 和 Default 模式均使用分类器 — Default 模式下只读/安全写入工具自动批准，其余仍需确认
        if (context.CurrentMode != PermissionMode.Auto && context.CurrentMode != PermissionMode.Default)
        {
            await next(context, ct).ConfigureAwait(false);
            return;
        }

        // 工具拒绝规则过滤
        if (_toolPermissionFilter is not null && _toolPermissionFilter.IsToolDenied(context.ToolName))
        {
            context.Result = ToolPermissionCheckResult.Rejected($"工具 '{context.ToolName}' 被拒绝规则过滤");
            return;
        }

        // 分类器判断 — ReadOnly/SafeWrite 工具自动批准，Sensitive/Unknown 仍需确认
        if (_autoModeClassifier is not null)
        {
            var request = new ClassificationRequest
            {
                ToolName = context.ToolName,
                Parameters = context.Arguments ?? new(),
                OperationType = OperationType.Auto
            };
            var classification = await _autoModeClassifier.ClassifyAsync(request, ct).ConfigureAwait(false);
            context.Result = classification.Action switch
            {
                SecurityAction.AutoApprove => ToolPermissionCheckResult.Approved(),
                SecurityAction.RequireConfirmation => ToolPermissionCheckResult.PendingConfirmation($"工具 '{context.ToolName}' 需要确认: {classification.Reason}"),
                SecurityAction.RequireApproval => ToolPermissionCheckResult.PendingConfirmation($"工具 '{context.ToolName}' 需要审批: {classification.Reason}"),
                SecurityAction.Block => ToolPermissionCheckResult.Rejected($"工具 '{context.ToolName}' 被阻止: {classification.Reason}"),
                _ => ToolPermissionCheckResult.Approved()
            };
            return;
        }

        await next(context, ct).ConfigureAwait(false);
    }
}
