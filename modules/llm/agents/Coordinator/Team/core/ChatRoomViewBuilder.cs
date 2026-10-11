namespace Core.Agents.Coordinator;

/// <summary>
/// 聊天室视图构建器 — 将团队状态转换为聊天室视图模型（含成员角色/在线数/最后消息时间）
/// </summary>
internal sealed class ChatRoomViewBuilder {
    private readonly TeamRegistry _registry;
    private readonly Func<string, CancellationToken, Task<IReadOnlyList<TeammateStatus>>> _getTeammateStatuses;

    /// <summary>
    /// 构造聊天室视图构建器实例
    /// </summary>
    /// <param name="registry">团队注册表</param>
    /// <param name="getTeammateStatuses">获取 Teammate 状态列表的委托</param>
    public ChatRoomViewBuilder(TeamRegistry registry, Func<string, CancellationToken, Task<IReadOnlyList<TeammateStatus>>> getTeammateStatuses) {
        _registry = registry;
        _getTeammateStatuses = getTeammateStatuses;
    }

    /// <summary>
    /// 获取聊天室信息 — 团队的聊天室视图，含房间 ID/成员角色/在线数/最后消息时间 — ADR 0109。
    /// </summary>
    public async Task<ChatRoomInfo?> GetChatRoomInfoAsync(
        string teamId,
        CancellationToken cancellationToken = default) {
        if (!_registry.TryGetRoom(teamId, out var room))
            return null;

        var team = room.Info;

        var statuses = await _getTeammateStatuses(teamId, cancellationToken).ConfigureAwait(false);
        var members = statuses.Select(s => new ChatRoomMember {
            AgentId = s.AgentId,
            DisplayName = s.DisplayName ?? s.AgentId,
            Role = MapToChatRoomRole(s.Role, s.AgentId, team.LeadAgentId),
            Status = MapToChatRoomMemberStatus(s),
            JoinedAt = room.MemberDetails.TryGetValue(s.AgentId, out var md) ? md.JoinedAt : DateTime.UtcNow,
        }).ToList();

        var onlineCount = members.Count(m => m.Status == ChatRoomMemberStatus.Online);
        var lastMessageAt = room.LastMessageAt;

        return new ChatRoomInfo {
            ChatRoomId = team.TeamId,
            RoomName = team.TeamName,
            Members = members,
            OnlineCount = onlineCount,
            LastMessageAt = lastMessageAt,
        };
    }

    private static ChatRoomRole MapToChatRoomRole(string? role, string agentId, string? leadAgentId) {
        if (agentId == leadAgentId) return ChatRoomRole.Owner;
        return role switch {
            "admin" => ChatRoomRole.Admin,
            _ => ChatRoomRole.Member,
        };
    }

    private static ChatRoomMemberStatus MapToChatRoomMemberStatus(TeammateStatus s) {
        if (!s.IsActive) return ChatRoomMemberStatus.Offline;
        return s.Status switch {
            AgentStatus.Running => ChatRoomMemberStatus.Online,
            _ => ChatRoomMemberStatus.Offline,
        };
    }
}