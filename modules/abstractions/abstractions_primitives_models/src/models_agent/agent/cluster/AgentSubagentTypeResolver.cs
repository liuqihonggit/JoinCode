namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 子代理类型标识 — 角色 + 可选变体。
/// </summary>
/// <param name="Role">代理角色。</param>
/// <param name="Variant">执行器变体；非 Executor 角色为 null。</param>
public readonly record struct AgentSubagentIdentity(AgentRole Role, ExecutorVariant? Variant);

/// <summary>
/// 子代理类型字符串解析 — 把 <c>executor:explore</c> / <c>plan</c> / <c>coordinator</c>
/// 统一映射为角色与变体，供 Agent 工具构造 SubAgentContext 与定义解析共用。
/// </summary>
public static class AgentSubagentTypeResolver {
    /// <summary>
    /// 解析子代理类型（支持逗号列表取首项）。
    /// </summary>
    /// <param name="subagentType">子代理类型字符串，如 <c>executor:explore</c>、<c>executor:search,worker</c>。</param>
    /// <returns>角色与变体；未知类型回退到无变体的 Executor。</returns>
    public static AgentSubagentIdentity Resolve(string? subagentType) {
        if (string.IsNullOrWhiteSpace(subagentType)) {
            return new AgentSubagentIdentity(AgentRole.Executor, null);
        }

        var primary = subagentType.Trim();
        var commaIndex = primary.IndexOf(',', StringComparison.Ordinal);
        if (commaIndex >= 0) {
            primary = primary[..commaIndex].Trim();
        }
        if (primary.Length == 0) {
            return new AgentSubagentIdentity(AgentRole.Executor, null);
        }

        var colonIndex = primary.IndexOf(':', StringComparison.Ordinal);
        if (colonIndex >= 0) {
            var roleText = primary[..colonIndex].Trim();
            var variantText = primary[(colonIndex + 1)..].Trim();
            var role = AgentRoleExtensions.FromValue(roleText) ?? AgentRole.Executor;
            var variant = ExecutorVariantExtensions.FromValue(variantText);
            return new AgentSubagentIdentity(role, variant);
        }

        var variantOnly = ExecutorVariantExtensions.FromValue(primary);
        if (variantOnly.HasValue) {
            return new AgentSubagentIdentity(AgentRole.Executor, variantOnly.Value);
        }

        var roleOnly = AgentRoleExtensions.FromValue(primary);
        if (roleOnly.HasValue) {
            return new AgentSubagentIdentity(roleOnly.Value, null);
        }

        return new AgentSubagentIdentity(AgentRole.Executor, null);
    }
}
