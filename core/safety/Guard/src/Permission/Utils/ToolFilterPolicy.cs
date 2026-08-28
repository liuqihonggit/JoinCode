namespace Core.Utils;

/// <summary>
/// 工具过滤策略实现 — 3 层收敛后的统一检查。
/// 检查顺序：Bypass → 层 1 全局禁用 → 层 2 白名单 → 层 3 代理黑名单 → 允许。
/// 对齐 TS 原版 的 filterToolsForAgent 3 层设计。
/// </summary>
[Register(typeof(IToolFilterPolicy), ServiceLifetime.Singleton)]
public sealed partial class ToolFilterPolicy : IToolFilterPolicy
{
    /// <inheritdoc />
    public ToolFilterResult Check(ToolFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Mode == PermissionMode.Bypass)
        {
            return ToolFilterResult.Allowed;
        }

        var toolName = context.ToolName;
        var disallowed = context.AllAgentDisallowedTools;

        if (disallowed.Contains(toolName))
        {
            return ToolFilterResult.Denied($"工具 '{toolName}' 被全局禁用（防递归）", 1);
        }

        if (disallowed.Contains("*"))
        {
            return ToolFilterResult.Denied($"工具 '{toolName}' 被通配符全局禁用", 1);
        }

        var allowed = context.AgentAllowedTools;
        if (allowed is { Count: > 0 } && !allowed.Contains(toolName))
        {
            return ToolFilterResult.Denied($"工具 '{toolName}' 不在代理工具白名单中", 2);
        }

        var agentDenied = context.AgentDisallowedTools;
        if (agentDenied is not null && agentDenied.Contains(toolName))
        {
            return ToolFilterResult.Denied($"工具 '{toolName}' 被代理定义禁用", 3);
        }

        return ToolFilterResult.Allowed;
    }
}

