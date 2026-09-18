namespace Core.Agents.Coordinator;

/// <summary>
/// 团队注册表 — 成对管理团队房间(ChatRoomState)与 agent→team 映射，
/// 提供按 teamId / agentId / sessionId / teamName 的查询入口。
/// <para>sessionId 与 teamName 索引字典提供 O(1) 查找,替代原 O(n) 线性扫描。</para>
/// <para>TryRemoveRoom 内聚清理 agent 映射与索引,保证一致性,调用方无需手动清理。</para>
/// </summary>
internal sealed class TeamRegistry
{
    private readonly ConcurrentDictionary<string, ChatRoomState> _rooms = new();
    private readonly ConcurrentDictionary<string, string> _agentToTeam = new();
    private readonly ConcurrentDictionary<string, string> _sessionIndex = new();
    private readonly ConcurrentDictionary<string, string> _nameIndex = new(StringComparer.OrdinalIgnoreCase);

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
    /// 添加或覆盖团队房间,同时维护 sessionId/teamName 索引。
    /// </summary>
    public void AddRoom(string teamId, ChatRoomState room)
    {
        if (_rooms.TryGetValue(teamId, out var oldRoom))
        {
            RemoveIndices(oldRoom);
        }
        _rooms[teamId] = room;
        AddIndices(teamId, room);
    }

    /// <summary>
    /// 移除团队房间,同时清理 agent 映射与索引,保证无孤儿映射。
    /// </summary>
    public bool TryRemoveRoom(string teamId, [MaybeNullWhen(false)] out ChatRoomState removedRoom)
    {
        if (!_rooms.TryRemove(teamId, out removedRoom))
            return false;

        foreach (var member in removedRoom.Members)
        {
            _agentToTeam.TryRemove(member, out _);
        }
        RemoveIndices(removedRoom);
        return true;
    }

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
    /// 按 sessionId 查找团队房间 — O(1) 索引查找。
    /// </summary>
    public ChatRoomState? FindRoomBySessionId(string sessionId)
        => _sessionIndex.TryGetValue(sessionId, out var teamId) && _rooms.TryGetValue(teamId, out var room)
            ? room
            : null;

    /// <summary>
    /// 按团队名称查找团队信息 — O(1) 索引查找（忽略大小写）。
    /// </summary>
    public TeamInfo? FindTeamByName(string teamName)
        => _nameIndex.TryGetValue(teamName, out var teamId) && _rooms.TryGetValue(teamId, out var room)
            ? room.Info
            : null;

    private void AddIndices(string teamId, ChatRoomState room)
    {
        if (room.SessionId is not null)
            _sessionIndex[room.SessionId] = teamId;
        _nameIndex[room.Info.TeamName] = teamId;
    }

    private void RemoveIndices(ChatRoomState room)
    {
        if (room.SessionId is not null)
            _sessionIndex.TryRemove(room.SessionId, out _);
        _nameIndex.TryRemove(room.Info.TeamName, out _);
    }
}
