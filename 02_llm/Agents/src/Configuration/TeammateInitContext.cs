namespace Core.Agents;

/// <summary>
/// 队友初始化上下文 - 包含队友加入团队时的初始化信息
/// </summary>
public sealed class TeammateInitContext
{
    public required string TeamId { get; init; }
    public required string TeamName { get; init; }
    public required string AgentId { get; init; }
    public string? AgentRole { get; init; }
    public IReadOnlyList<string> OtherMembers { get; init; } = [];
    public string? TeamDescription { get; init; }
    public string? CoordinatorId { get; init; }
    public string? LeadAgentId { get; init; }
    public IReadOnlyList<TeamAllowedPath> AllowedPaths { get; init; } = [];
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;

    public Dictionary<string, string> ToEnvironmentVariables()
    {
        var env = new Dictionary<string, string>
        {
            [JccEnvVar.TeamId.ToValue()] = TeamId,
            [JccEnvVar.TeamName.ToValue()] = TeamName,
            [JccEnvVar.TeammateId.ToValue()] = AgentId
        };

        if (!string.IsNullOrEmpty(AgentRole))
            env[JccEnvVar.TeammateRole.ToValue()] = AgentRole;

        if (!string.IsNullOrEmpty(CoordinatorId))
            env[JccEnvVar.CoordinatorId.ToValue()] = CoordinatorId;

        if (!string.IsNullOrEmpty(LeadAgentId))
            env[JccEnvVar.LeadAgentId.ToValue()] = LeadAgentId;

        if (AllowedPaths.Count > 0)
            env[JccEnvVar.TeamAllowedPaths.ToValue()] = string.Join(";", AllowedPaths.Select(p => $"{p.Path}:{p.AccessLevel}"));

        return env;
    }

    public string BuildContextSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"你是团队 \"{TeamName}\" 的成员。");
        sb.AppendLine($"团队ID: {TeamId}");
        sb.AppendLine($"你的ID: {AgentId}");

        if (!string.IsNullOrEmpty(AgentRole))
            sb.AppendLine($"你的角色: {AgentRole}");

        if (!string.IsNullOrEmpty(TeamDescription))
            sb.AppendLine($"团队描述: {TeamDescription}");

        if (!string.IsNullOrEmpty(CoordinatorId))
            sb.AppendLine($"协调器ID: {CoordinatorId}");

        if (!string.IsNullOrEmpty(LeadAgentId))
            sb.AppendLine($"Leader ID: {LeadAgentId}");

        if (OtherMembers.Count > 0)
        {
            sb.AppendLine($"其他成员: {string.Join(", ", OtherMembers)}");
        }

        if (AllowedPaths.Count > 0)
        {
            sb.AppendLine("允许路径:");
            foreach (var path in AllowedPaths)
            {
                sb.AppendLine($"  {path.Path} ({path.AccessLevel})");
            }
        }

        return sb.ToString();
    }
}

/// <summary>
/// 队友初始化服务接口 - 构建队友加入团队时的初始化上下文并注册钩子
/// </summary>
public interface ITeammateInitService
{
    /// <summary>
    /// 构建队友初始化上下文
    /// </summary>
    Task<TeammateInitContext?> BuildInitContextAsync(string teamId, string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 初始化队友钩子：应用团队权限 + 注册 Stop Hook 用于空闲通知
    /// </summary>
    Task InitializeTeammateHooksAsync(string teamId, string agentId, string sessionId, CancellationToken cancellationToken = default);
}
