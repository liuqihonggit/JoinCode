namespace JoinCode.Abstractions.Models.Agent;

public sealed record TeamAllowedPath {
    /// <summary>获取允许的路径。</summary>
    public required string Path { get; init; }
    /// <summary>获取访问级别。</summary>
    public AccessLevel AccessLevel { get; init; } = AccessLevel.Read;
}

public sealed record TeamMemberInfo {
    /// <summary>获取 Agent 标识。</summary>
    public required string AgentId { get; init; }
    /// <summary>获取角色。</summary>
    public string? Role { get; init; }
    /// <summary>获取是否活跃。</summary>
    public bool IsActive { get; init; } = true;
    /// <summary>获取加入时间。</summary>
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;
    /// <summary>获取颜色标识。</summary>
    public string? Color { get; init; }
}

/// <summary>
/// 团队信息
/// </summary>
public sealed record TeamInfo {
    /// <summary>
    /// 团队ID
    /// </summary>
    public required string TeamId { get; init; }

    /// <summary>
    /// 团队名称
    /// </summary>
    public required string TeamName { get; init; }

    /// <summary>
    /// 团队描述
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// 团队Leader的AgentId
    /// </summary>
    public string? LeadAgentId { get; init; }

    /// <summary>
    /// 成员列表
    /// </summary>
    public IReadOnlyCollection<string> Members { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 成员详细信息
    /// </summary>
    public IReadOnlyList<TeamMemberInfo> MemberDetails { get; init; } = Array.Empty<TeamMemberInfo>();

    /// <summary>
    /// 团队级允许路径
    /// </summary>
    public IReadOnlyList<TeamAllowedPath> AllowedPaths { get; init; } = Array.Empty<TeamAllowedPath>();

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 最后活动时间
    /// </summary>
    public DateTime LastActivityAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 团队消息
/// </summary>
public sealed record TeamMessage {
    /// <summary>
    /// 消息ID
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// 团队ID
    /// </summary>
    public required string TeamId { get; init; }

    /// <summary>
    /// 发送者ID
    /// </summary>
    public required string SenderId { get; init; }

    /// <summary>
    /// 消息内容
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// 消息类型
    /// </summary>
    public string MessageType { get; init; } = AgentOutputChunkType.Text.ToValue();

    /// <summary>
    /// 发送时间
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 是否已读
    /// </summary>
    public bool IsRead { get; set; }

    /// <summary>消息可见性 — 控制投递范围，对标 QQ 系统消息/私信/撤回 — ADR 0109 决策8。</summary>
    public MessageVisibility Visibility { get; init; } = MessageVisibility.Public;

    /// <summary>私信目标 AgentId（仅 Visibility=Private 时有效）</summary>
    public string? ToAgentId { get; init; }

    /// <summary>撤回原因（仅 Visibility=Hidden 时有效，记录撤回原因供审计）</summary>
    public string? RevokeReason { get; init; }

    /// <summary>@提及的 AgentId 列表（null 表示无 @提及）— ADR 0109 决策12。</summary>
    public IReadOnlyList<string>? Mentions { get; init; }
}