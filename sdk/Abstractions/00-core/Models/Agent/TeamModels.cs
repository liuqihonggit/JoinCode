namespace JoinCode.Abstractions.Models.Agent;

public sealed record TeamAllowedPath
{
    public required string Path { get; init; }
    public AccessLevel AccessLevel { get; init; } = AccessLevel.Read;
}

public sealed record TeamMemberInfo
{
    public required string AgentId { get; init; }
    public string? Role { get; init; }
    public bool IsActive { get; init; } = true;
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;
    public string? Color { get; init; }
}

/// <summary>
/// 团队信息
/// </summary>
public sealed record TeamInfo
{
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
    public IReadOnlyList<string> Members { get; init; } = Array.Empty<string>();

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
public sealed record TeamMessage
{
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
    public string MessageType { get; init; } = "text";

    /// <summary>
    /// 发送时间
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 是否已读
    /// </summary>
    public bool IsRead { get; set; }
}

/// <summary>
/// 团队操作结果
/// </summary>
public sealed record TeamOperationResult
{
    /// <summary>
    /// 是否成功
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// 团队信息
    /// </summary>
    public TeamInfo? Team { get; init; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; init; }

    public TeamOperationResult(bool success, TeamInfo? team = null, string? errorMessage = null)
    {
        Success = success;
        Team = team;
        ErrorMessage = errorMessage;
    }
}
