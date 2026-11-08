namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 系统通知类型 — 对标 QQ 系统消息（加入/退出/禁言/踢人/撤回等）— ADR 0109 决策9。
/// </summary>
public enum SystemNoticeKind {
    /// <summary>"xxx 加入聊天室" — System 可见性，所有人可见</summary>
    [EnumValue("member_joined")] MemberJoined = 0,

    /// <summary>"xxx 退出聊天室" — System 可见性，所有人可见</summary>
    [EnumValue("member_left")] MemberLeft = 1,

    /// <summary>"xxx 被禁言" — AdminOnly 可见性，仅管理员可见</summary>
    [EnumValue("member_muted")] MemberMuted = 2,

    /// <summary>"xxx 被解除禁言" — AdminOnly 可见性，仅管理员可见</summary>
    [EnumValue("member_unmuted")] MemberUnmuted = 3,

    /// <summary>"xxx 被踢出" — AdminOnly 可见性，仅管理员可见</summary>
    [EnumValue("member_kicked")] MemberKicked = 4,

    /// <summary>"xxx 被设为管理员" — AdminOnly 可见性，仅管理员可见</summary>
    [EnumValue("role_promoted")] RolePromoted = 5,

    /// <summary>"xxx 被取消管理员" — AdminOnly 可见性，仅管理员可见</summary>
    [EnumValue("role_demoted")] RoleDemoted = 6,

    /// <summary>"主机切换：xxx → yyy" — System 可见性，跨进程选举通知</summary>
    [EnumValue("host_changed")] HostChanged = 7,

    /// <summary>"全局编译队列繁忙，排队中" — System 可见性</summary>
    [EnumValue("build_queue_busy")] BuildQueueBusy = 8,

    /// <summary>"xxx 撤回了一条消息" — System 可见性，所有人可见</summary>
    [EnumValue("message_revoked")] MessageRevoked = 9,
}

/// <summary>
/// 系统通知消息工厂 — 集中构造系统通知 TeamMessage，禁止散落构造 — ADR 0109 决策9。
/// <para>每种 <see cref="SystemNoticeKind"/> 对应固定的 content 模板和 <see cref="MessageVisibility"/>，</para>
/// <para>确保系统通知的可见性策略一致，不会因调用方疏忽导致系统通知泄露给非授权成员。</para>
/// </summary>
public static class SystemNoticeFactory {
    /// <summary>
    /// 创建系统通知消息。
    /// </summary>
    /// <param name="kind">系统通知类型</param>
    /// <param name="teamId">团队/聊天室 ID</param>
    /// <param name="actorAgentId">操作主体 AgentId（如加入者/被禁言者/撤回者）</param>
    /// <param name="extra">附加信息（如 HostChanged 的新主机 ID、BuildQueueBusy 的排队位置）</param>
    /// <returns>构造好的 <see cref="TeamMessage"/>，MessageType="system_notice"，SenderId="system"</returns>
    public static TeamMessage Create(SystemNoticeKind kind, string teamId, string actorAgentId, string? extra = null) {
        var (content, visibility) = kind switch {
            SystemNoticeKind.MemberJoined => ($"{actorAgentId} 加入聊天室", MessageVisibility.System),
            SystemNoticeKind.MemberLeft => ($"{actorAgentId} 退出聊天室", MessageVisibility.System),
            SystemNoticeKind.MemberMuted => ($"{actorAgentId} 被禁言", MessageVisibility.AdminOnly),
            SystemNoticeKind.MemberUnmuted => ($"{actorAgentId} 被解除禁言", MessageVisibility.AdminOnly),
            SystemNoticeKind.MemberKicked => ($"{actorAgentId} 被踢出", MessageVisibility.AdminOnly),
            SystemNoticeKind.RolePromoted => ($"{actorAgentId} 被设为管理员", MessageVisibility.AdminOnly),
            SystemNoticeKind.RoleDemoted => ($"{actorAgentId} 被取消管理员", MessageVisibility.AdminOnly),
            SystemNoticeKind.HostChanged => ($"主机切换：{actorAgentId} → {extra ?? "未知"}", MessageVisibility.System),
            SystemNoticeKind.BuildQueueBusy => ($"全局编译队列繁忙，FIFO 排队中（当前位置：{extra ?? "未知"}）", MessageVisibility.System),
            SystemNoticeKind.MessageRevoked => ($"{actorAgentId} 撤回了一条消息", MessageVisibility.System),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown system notice kind"),
        };

        return new TeamMessage {
            MessageId = Guid.NewGuid().ToString("N"),
            TeamId = teamId,
            SenderId = "system",
            Content = content,
            MessageType = "system_notice",
            Visibility = visibility,
            Timestamp = DateTime.UtcNow,
        };
    }
}