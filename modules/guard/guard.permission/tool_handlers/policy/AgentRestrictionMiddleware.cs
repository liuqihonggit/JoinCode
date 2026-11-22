
namespace Core.Permission;

/// <summary>
/// Agent 工具限制中间件 — 在 Auto/Unattended 等模式下检查 Agent 工具限制。
/// 内部通过 IToolFilterPolicy 统一 3 层过滤检查（对齐 TS 原版 filterToolsForAgent）。
/// </summary>
[Register(typeof(IPermissionMiddleware), ServiceLifetime.Singleton)]
public sealed partial class AgentRestrictionMiddleware : ServiceEntity, IPermissionMiddleware {
    private readonly IAgentToolRestrictions? _agentToolRestrictions;
    private readonly IToolFilterPolicy? _toolFilterPolicy;

    /// <summary>
    /// 创建 AgentRestrictionMiddleware
    /// </summary>
    public AgentRestrictionMiddleware(
        IAgentToolRestrictions? agentToolRestrictions = null,
        IToolFilterPolicy? toolFilterPolicy = null) {
        _agentToolRestrictions = agentToolRestrictions;
        _toolFilterPolicy = toolFilterPolicy;
    }

    /// <inheritdoc />
    public Task InvokeAsync(PermissionCheckContext context, MiddlewareDelegate<PermissionCheckContext> next, CancellationToken ct) {
        if (context.CurrentMode == PermissionMode.Bypass)
            return next(context, ct);

        if (_agentToolRestrictions is null && context.AllowedTools is null && context.DeniedTools is null)
            return next(context, ct);

        var agentMode = context.CurrentMode;

        if (_toolFilterPolicy is not null) {
            var deniedByMode = _agentToolRestrictions?.GetDeniedTools(agentMode) ?? FrozenSet<string>.Empty;
            var deniedTools = context.DeniedTools is { Count: > 0 }
                ? deniedByMode.Union(context.DeniedTools).ToFrozenSet(StringComparer.OrdinalIgnoreCase)
                : deniedByMode;
            var filterContext = new ToolFilterContext(
                context.ToolName,
                agentMode,
                FrozenSet<string>.Empty,
                context.AllowedTools,
                deniedTools);
            var filterResult = _toolFilterPolicy.Check(filterContext);
            if (!filterResult.IsAllowed) {
                context.Result = ToolPermissionCheckResult.Rejected(filterResult.Reason ?? $"工具 '{context.ToolName}' 在当前权限模式下不被允许");
                return Task.CompletedTask;
            }
            return next(context, ct);
        }

        if (!IsAllowedByModeOrContext(context, agentMode)) {
            context.Result = ToolPermissionCheckResult.Rejected($"工具 '{context.ToolName}' 在当前权限模式下不被允许");
            return Task.CompletedTask;
        }

        return next(context, ct);
    }

    /// <summary>无 ToolFilterPolicy 时的回退检查 — 模式限制 + 子代理白名单/拒绝集合。</summary>
    private bool IsAllowedByModeOrContext(PermissionCheckContext context, PermissionMode agentMode) {
        if (_agentToolRestrictions is not null && !_agentToolRestrictions.IsToolAllowedForMode(context.ToolName, agentMode))
            return false;
        if (context.AllowedTools is { Count: > 0 } && !context.AllowedTools.Contains(context.ToolName))
            return false;
        return context.DeniedTools is not { Count: > 0 } || !context.DeniedTools.Contains(context.ToolName);
    }
}