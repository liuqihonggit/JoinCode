namespace Core.Agents.Coordinator;

/// <summary>
/// 团队注册表 — 成对管理团队房间(ChatRoomState)与 agent→team 映射，
/// 提供按 teamId / agentId / sessionId / teamName 的查询入口。
/// </summary>
internal sealed class TeamRegistry
{
    private readonly ConcurrentDictionary<string, ChatRoomState> _rooms = new();
    private readonly ConcurrentDictionary<string, string> _agentToTeam = new();

    /// <summary>
    /// 所有团队房间视图 — 用于遍历查询（不要在此视图上做写操作）。
    /// </summary>
    public IEnumerable<ChatRoomState> Rooms => _rooms.Values;

    /// <summary>
    /// 已注册团队数量。
    /// </summary>
    public int Count => _rooms.Count;

    /// <summary>
    /// 团队房间快照 — 用于持久化序列化。值为引用类型，修改快照中的 room 会反映到原注册表。
    /// </summary>
    public Dictionary<string, ChatRoomState> SnapshotRooms()
        => _rooms.ToDictionary();

    /// <summary>
    /// agent→team 映射快照 — 用于持久化序列化。
    /// </summary>
    public IReadOnlyDictionary<string, string> SnapshotAgentToTeam()
        => new Dictionary<string, string>(_agentToTeam);

    /// <summary>
    /// 查找指定团队房间。
    /// </summary>
    public bool TryGetRoom(string teamId, out ChatRoomState room)
        => _rooms.TryGetValue(teamId, out room!);

    /// <summary>
    /// 查找 agent 所属的 teamId。
    /// </summary>
    public bool TryGetTeamIdForAgent(string agentId, [MaybeNullWhen(false)] out string teamId)
        => _agentToTeam.TryGetValue(agentId, out teamId);

    /// <summary>
    /// 添加或覆盖团队房间。
    /// </summary>
    public void AddRoom(string teamId, ChatRoomState room)
        => _rooms[teamId] = room;

    /// <summary>
    /// 移除团队房间，返回被移除的房间。
    /// </summary>
    public bool TryRemoveRoom(string teamId, [MaybeNullWhen(false)] out ChatRoomState removedRoom)
        => _rooms.TryRemove(teamId, out removedRoom);

    /// <summary>
    /// 注册 agent→team 映射。
    /// </summary>
    public void RegisterAgentToTeam(string agentId, string teamId)
        => _agentToTeam[agentId] = teamId;

    /// <summary>
    /// 注销 agent→team 映射。
    /// </summary>
    public bool UnregisterAgentFromTeam(string agentId)
        => _agentToTeam.TryRemove(agentId, out _);

    /// <summary>
    /// 按 sessionId 查找团队房间 — 用于单团队限制检查。
    /// </summary>
    public ChatRoomState? FindRoomBySessionId(string sessionId)
        => _rooms.FirstOrDefault(kvp => kvp.Value.SessionId == sessionId).Value;

    /// <summary>
    /// 按团队名称查找团队信息 — 用于名称唯一性检查（忽略大小写）。
    /// </summary>
    public TeamInfo? FindTeamByName(string teamName)
        => _rooms.Values.Select(r => r.Info)
            .FirstOrDefault(t => string.Equals(t.TeamName, teamName, StringComparison.OrdinalIgnoreCase));
}
