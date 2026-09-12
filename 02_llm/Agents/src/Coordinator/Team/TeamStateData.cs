namespace Core.Agents.Coordinator;

/// <summary>
/// 团队持久化状态 DTO — 序列化到 ~/.jcc/teams/state.json
/// 用于 CLI 无状态模式（mcp_call 单次调用）下跨进程共享团队状态
/// </summary>
public sealed class TeamStateData
{
    /// <summary>团队列表</summary>
    public List<TeamInfo> Teams { get; set; } = [];

    /// <summary>团队成员映射 (teamId → member agentIds)</summary>
    public Dictionary<string, List<string>> TeamMembers { get; set; } = [];

    /// <summary>团队消息映射 (teamId → messages)</summary>
    public Dictionary<string, List<TeamMessage>> TeamMessages { get; set; } = [];

    /// <summary>团队成员详情映射 (teamId → member details)</summary>
    public Dictionary<string, List<TeamMemberInfo>> TeamMemberDetails { get; set; } = [];

    /// <summary>代理到团队的映射 (agentId → teamId)</summary>
    public Dictionary<string, string> AgentToTeam { get; set; } = [];

    /// <summary>团队 ID 计数器</summary>
    public int TeamCounter { get; set; }

    /// <summary>消息 ID 计数器</summary>
    public int MessageCounter { get; set; }
}
