namespace Core.Agents;


/// <summary>
/// 插件 Agent 安全限制校验 — 效应分类器
/// <para>对齐 TS 原版 安装时信任边界: 插件 agent 不能定义 permissionMode/hooks/mcpServers</para>
/// <para>对齐 Cordis 框架效应分类: 插件 agent 只能产生安全效应，不能产生特权效应</para>
/// </summary>
public static class PluginAgentValidator
{
    /// <summary>
    /// 校验插件 agent 定义，违反安全限制则抛 InvalidOperationException
    /// </summary>
    public static void Validate(AgentDefinition definition)
    {
        if (!string.IsNullOrWhiteSpace(definition.PermissionMode))
        {
            throw new InvalidOperationException(
                $"插件 agent '{definition.DisplayId}' 不能定义 permissionMode（安装时信任边界，不允许单个 agent 文件静默添加权限模式）");
        }

        if (definition.Hooks is not null && definition.Hooks.Count > 0)
        {
            throw new InvalidOperationException(
                $"插件 agent '{definition.DisplayId}' 不能定义 hooks（安装时信任边界，不允许单个 agent 文件静默添加钩子）");
        }

        if (definition.McpServers is not null && definition.McpServers.Count > 0)
        {
            throw new InvalidOperationException(
                $"插件 agent '{definition.DisplayId}' 不能定义 mcpServers（安装时信任边界，不允许单个 agent 文件静默添加 MCP 服务器）");
        }
    }

    /// <summary>
    /// 批量校验，返回所有违规消息（不抛异常）
    /// </summary>
    public static IReadOnlyList<string> ValidateAll(IReadOnlyList<AgentDefinition> definitions)
    {
        var violations = new List<string>();
        foreach (var def in definitions)
        {
            if (!string.IsNullOrWhiteSpace(def.PermissionMode))
                violations.Add($"'{def.DisplayId}': permissionMode 禁止");

            if (def.Hooks is not null && def.Hooks.Count > 0)
                violations.Add($"'{def.DisplayId}': hooks 禁止");

            if (def.McpServers is not null && def.McpServers.Count > 0)
                violations.Add($"'{def.DisplayId}': mcpServers 禁止");
        }
        return violations;
    }
}
