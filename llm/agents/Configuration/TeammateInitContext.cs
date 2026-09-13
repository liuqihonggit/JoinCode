namespace Core.Agents;

/// <summary>
/// 队友初始化上下文 - 包含队友加入团队时的初始化信息
/// </summary>
public sealed class TeammateInitContext
{
    /// <summary>团队唯一标识</summary>
    public required string TeamId { get; init; }
    /// <summary>团队名称</summary>
    public required string TeamName { get; init; }
    /// <summary>当前队友的 Agent 唯一标识</summary>
    public required string AgentId { get; init; }
    /// <summary>当前队友的角色名称（可选）</summary>
    public string? AgentRole { get; init; }
    /// <summary>团队中其他成员的标识列表</summary>
    public IReadOnlyList<string> OtherMembers { get; init; } = [];
    /// <summary>团队描述（可选）</summary>
    public string? TeamDescription { get; init; }
    /// <summary>协调器 Agent 标识（可选）</summary>
    public string? CoordinatorId { get; init; }
    /// <summary>Leader Agent 标识（可选）</summary>
    public string? LeadAgentId { get; init; }
    /// <summary>团队允许访问的路径及访问级别列表</summary>
    public IReadOnlyList<TeamAllowedPath> AllowedPaths { get; init; } = [];
    /// <summary>队友加入团队的时间戳（UTC）</summary>
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 将上下文转换为环境变量字典，供子进程注入团队信息
    /// </summary>
    /// <returns>包含团队相关环境变量的字典</returns>
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

    /// <summary>
    /// 构建可读的上下文摘要文本，用于向队友展示团队信息
    /// </summary>
    /// <returns>包含团队、角色、成员、允许路径等信息的摘要字符串</returns>
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
