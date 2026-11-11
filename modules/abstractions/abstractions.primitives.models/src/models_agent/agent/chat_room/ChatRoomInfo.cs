namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 聊天室成员角色 — 对标 QQ 群角色（群主/管理员/普通成员）— ADR 0109 决策7。
/// </summary>
public enum ChatRoomRole {
    /// <summary>群主（最高权限，可转让）</summary>
    [EnumValue("owner")] Owner = 0,

    /// <summary>管理员（可禁言/踢人/撤回他人消息）</summary>
    [EnumValue("admin")] Admin = 1,

    /// <summary>普通成员（可发消息/私信）</summary>
    [EnumValue("member")] Member = 2,
}

/// <summary>
/// 聊天室成员在线状态 — 对标 QQ 在线/离线/禁言 — ADR 0109 决策7。
/// </summary>
public enum ChatRoomMemberStatus {
    /// <summary>在线</summary>
    [EnumValue("online")] Online = 0,

    /// <summary>离线</summary>
    [EnumValue("offline")] Offline = 1,

    /// <summary>被禁言（可看不能发）</summary>
    [EnumValue("muted")] Muted = 2,
}

/// <summary>
/// 聊天室成员信息 — 含角色/状态/显示名，对标 QQ 群成员 — ADR 0109 决策7。
/// </summary>
public sealed record ChatRoomMember {
    /// <summary>成员 Agent 标识</summary>
    public required string AgentId { get; init; }

    /// <summary>成员显示名（bot 中文名或用户指定名）</summary>
    public required string DisplayName { get; init; }

    /// <summary>成员角色（群主/管理员/普通成员）</summary>
    public ChatRoomRole Role { get; init; } = ChatRoomRole.Member;

    /// <summary>成员在线状态</summary>
    public ChatRoomMemberStatus Status { get; init; } = ChatRoomMemberStatus.Online;

    /// <summary>加入时间</summary>
    public DateTime JoinedAt { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 聊天室信息 — 团队的聊天室视图，含房间 ID/成员列表/角色/在线数 — ADR 0109。
/// <para>对标 QQ 群信息：唯一群号 + 成员角色 + 在线状态 + 最后消息时间。</para>
/// </summary>
public sealed record ChatRoomInfo {
    /// <summary>聊天室唯一标识（等同 TeamId，复用不新建 ID 体系）— ADR 0109 决策7。</summary>
    public required string ChatRoomId { get; init; }

    /// <summary>聊天室名称（等同团队名）</summary>
    public required string RoomName { get; init; }

    /// <summary>聊天室成员列表（含角色/状态/显示名）</summary>
    public required IReadOnlyList<ChatRoomMember> Members { get; init; }

    /// <summary>当前查询者的角色（null 表示非成员或未查询）— 用于前端按角色显示操作按钮。</summary>
    public ChatRoomRole? MyRole { get; init; }

    /// <summary>在线成员数</summary>
    public int OnlineCount { get; init; }

    /// <summary>最后消息时间（null 表示无消息）</summary>
    public DateTime? LastMessageAt { get; init; }

    /// <summary>成员数量</summary>
    public int MemberCount => Members.Count;
}