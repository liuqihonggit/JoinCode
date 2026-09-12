namespace JoinCode.Abstractions.Interfaces;

public interface IAgentPermissionManager
{
    Task AddRuleAsync(AgentPermissionRule rule, CancellationToken ct = default);
    Task<bool> RemoveRuleAsync(string agentPattern, CancellationToken ct = default);
    Task<PermissionCheckResult> CheckToolPermissionAsync(string agentName, string toolName, Dictionary<string, JsonElement>? parameters = null, CancellationToken ct = default);
    Task<PermissionCheckResult> CheckPathPermissionAsync(string agentName, string path, CancellationToken ct = default);
    Task<AgentPermissionRule?> GetRuleForAgentAsync(string agentName, CancellationToken ct = default);
    Task<IReadOnlyList<AgentPermissionRule>> ListRulesAsync(CancellationToken ct = default);
    Task ClearRulesAsync(CancellationToken ct = default);
}
