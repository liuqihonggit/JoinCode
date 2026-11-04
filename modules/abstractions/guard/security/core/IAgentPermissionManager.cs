namespace JoinCode.Abstractions.Interfaces;

public interface IAgentPermissionManager {
    /// <summary>异步添加权限规则。</summary>
    /// <param name="rule">权限规则。</param>
    /// <param name="ct">取消令牌。</param>
    Task AddRuleAsync(AgentPermissionRule rule, CancellationToken ct = default);
    /// <summary>异步按 Agent 模式移除规则。</summary>
    /// <param name="agentPattern">Agent 匹配模式。</param>
    /// <param name="ct">取消令牌。</param>
    Task<bool> RemoveRuleAsync(string agentPattern, CancellationToken ct = default);
    /// <summary>异步检查工具调用权限。</summary>
    /// <param name="agentName">Agent 名称。</param>
    /// <param name="toolName">工具名称。</param>
    /// <param name="parameters">调用参数。</param>
    /// <param name="ct">取消令牌。</param>
    Task<PermissionCheckResult> CheckToolPermissionAsync(string agentName, string toolName, Dictionary<string, JsonElement>? parameters = null, CancellationToken ct = default);
    /// <summary>异步检查路径访问权限。</summary>
    /// <param name="agentName">Agent 名称。</param>
    /// <param name="path">路径。</param>
    /// <param name="ct">取消令牌。</param>
    Task<PermissionCheckResult> CheckPathPermissionAsync(string agentName, string path, CancellationToken ct = default);
    /// <summary>异步获取指定 Agent 的权限规则。</summary>
    /// <param name="agentName">Agent 名称。</param>
    /// <param name="ct">取消令牌。</param>
    Task<AgentPermissionRule?> GetRuleForAgentAsync(string agentName, CancellationToken ct = default);
    /// <summary>异步列出所有权限规则。</summary>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<AgentPermissionRule>> ListRulesAsync(CancellationToken ct = default);
    /// <summary>异步清空所有权限规则。</summary>
    /// <param name="ct">取消令牌。</param>
    Task ClearRulesAsync(CancellationToken ct = default);
}