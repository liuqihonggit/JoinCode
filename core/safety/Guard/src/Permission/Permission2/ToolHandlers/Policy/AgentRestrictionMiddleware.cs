
namespace Core.Permission;

/// <summary>
/// Agent 工具限制中间件 — Default 模式下检查 Agent 工具限制。
/// 内部通过 IToolFilterPolicy 统一 3 层过滤检查（对齐 claude code filterToolsForAgent）。
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class AgentRestrictionMiddleware : ServiceEntity, IPermissionMiddleware
{
    private readonly IAgentToolRestrictions? _agentToolRestrictions;
    private readonly IToolFilterPolicy? _toolFilterPolicy;

    /// <summary>
    /// 创建 AgentRestrictionMiddleware
    /// </summary>
    public AgentRestrictionMiddleware(
        IAgentToolRestrictions? agentToolRestrictions = null,
        IToolFilterPolicy? toolFilterPolicy = null)
    {
        _agentToolRestrictions = agentToolRestrictions;
        _toolFilterPolicy = toolFilterPolicy;
    }

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        if (context.CurrentMode == PermissionMode.Bypass || _agentToolRestrictions is null)
            return next(context, ct);

        if (context.AutoApprovedTools.Contains(context.ToolName))
            return next(context, ct);

        var agentMode = context.CurrentMode;

        if (_toolFilterPolicy is not null)
        {
            var deniedTools = _agentToolRestrictions.GetDeniedTools(agentMode);
            var filterContext = new ToolFilterContext(
                context.ToolName,
                agentMode,
                deniedTools,
                null,
                null);
            var filterResult = _toolFilterPolicy.Check(filterContext);
            if (!filterResult.IsAllowed)
            {
                context.Result = ToolPermissionCheckResult.Rejected(filterResult.Reason ?? $"工具 '{context.ToolName}' 在当前权限模式下不被允许");
                return Task.CompletedTask;
            }
            return next(context, ct);
        }

        if (!_agentToolRestrictions.IsToolAllowedForMode(context.ToolName, agentMode))
        {
            var deniedTools = _agentToolRestrictions.GetDeniedTools(agentMode);
            var hint = deniedTools.Contains(context.ToolName)
                ? $" [调试: 工具 '{context.ToolName}' 在 {agentMode} 模式下被限制。--trust只跳过目录信任，不改变权限模式。需要 --bypass 或 --permission-mode {PermissionMode.Bypass.ToValue()} 或设置 JCC_PERMISSION_MODE={PermissionMode.Bypass.ToValue()}]"
                : "";
            context.Result = ToolPermissionCheckResult.Rejected($"工具 '{context.ToolName}' 在当前权限模式下不被允许{hint}");
            return Task.CompletedTask;
        }

        return next(context, ct);
    }
}
