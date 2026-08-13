
namespace Core.Permission;

/// <summary>
/// Agent 工具限制中间件 — Default 模式下检查 Agent 工具限制
/// </summary>
[Register(typeof(IPermissionMiddleware))]
public sealed partial class AgentRestrictionMiddleware : ServiceEntity, IPermissionMiddleware
{
    private readonly IAgentToolRestrictions? _agentToolRestrictions;

    /// <inheritdoc />

    /// <inheritdoc />

    /// <summary>
    /// 创建 AgentRestrictionMiddleware
    /// </summary>
    public AgentRestrictionMiddleware(IAgentToolRestrictions? agentToolRestrictions = null)
    {
        _agentToolRestrictions = agentToolRestrictions;
    }

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct)
    {
        if (context.CurrentMode == PermissionMode.Bypass || _agentToolRestrictions is null)
            return next(context, ct);

        // 用户显式 allow 列表优先 — 绕过硬编码 Agent 限制
        // 对齐 TS: permissions.allow 中的工具不应被 AgentRestrictions 拒绝
        // 场景: 用户配置 permissions.allow: ["Bash"]，但 Bash 在 AutoDeniedTools 中，
        // 若不先检查 AutoApprovedTools，会短路拒绝，ToolListPermissionMiddleware (Order=700) 永远执行不到
        if (context.AutoApprovedTools.Contains(context.ToolName))
            return next(context, ct);

        var agentMode = context.CurrentMode;
        if (!_agentToolRestrictions.IsToolAllowedForMode(context.ToolName, agentMode))
        {
            var deniedTools = _agentToolRestrictions.GetDeniedTools(agentMode);
            var hint = deniedTools.Contains(context.ToolName)
                ? $" [调试: 工具 '{context.ToolName}' 在 {agentMode} 模式下被限制。--trust只跳过目录信任，不改变权限模式。需要 --dangerously-skip-permissions 或 --permission-mode bypassPermissions 或设置 JCC_PERMISSION_MODE=bypassPermissions]"
                : "";
            context.Result = ToolPermissionCheckResult.Rejected($"工具 '{context.ToolName}' 在当前权限模式下不被允许{hint}");
            return Task.CompletedTask;
        }

        return next(context, ct);
    }
}
