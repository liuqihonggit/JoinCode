namespace Core.Agents.Coordinator;

/// <summary>
/// 团队注册表 — 成对管理团队房间(ChatRoomState)与 agent→team 映射，
/// 提供按 teamId / agentId / sessionId / teamName 的查询入口。
/// <para>唯一数据源 _rooms(ImmutableDictionary) + 2 个冗余查询索引 _bySessionId/_byTeamName(O(1)查询),写入时同步更新,读取时验证一致性。</para>
/// <para>TryRemoveRoom 内聚清理 agent 映射 + 冗余索引,保证一致性,调用方无需手动清理。</para>
/// </summary>
internal sealed class TeamRegistry {
    private ImmutableDictionary<string, ChatRoomState> _rooms = ImmutableDictionary<string, ChatRoomState>.Empty;
    private ImmutableDictionary<string, string> _agentToTeam = ImmutableDictionary<string, string>.Empty;
    private ImmutableDictionary<string, string> _bySessionId = ImmutableDictionary<string, string>.Empty;
    private ImmutableDictionary<string, string> _byTeamName = ImmutableDictionary<string, string>.Empty;

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
    /// agent→team 映射快照 — 用于持久化序列化。返回不可变引用,无需拷贝。
    /// </summary>
    public IReadOnlyDictionary<string, string> SnapshotAgentToTeam()
        => _agentToTeam;

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
    /// 添加或覆盖团队房间 — 原子 SetItem,同步更新冗余查询索引。
    /// </summary>
    public void AddRoom(string teamId, ChatRoomState room) {
        var oldRoom = _rooms.TryGetValue(teamId, out var existing) ? existing : null;

        ImmutableInterlocked.Update(ref _rooms, static (dict, arg) => dict.SetItem(arg.teamId, arg.room), (teamId, room));

        if (oldRoom is not null && oldRoom.SessionId != room.SessionId)
            ImmutableInterlocked.Update(ref _bySessionId, static (dict, oldSid) => dict.Remove(oldSid), oldRoom.SessionId!);
        ImmutableInterlocked.Update(ref _bySessionId, static (dict, arg) => dict.SetItem(arg.sid, arg.teamId), (sid: room.SessionId!, teamId));

        var oldNameKey = oldRoom?.Info.TeamName.ToLowerInvariant();
        var newNameKey = room.Info.TeamName.ToLowerInvariant();
        if (oldNameKey is not null && oldNameKey != newNameKey)
            ImmutableInterlocked.Update(ref _byTeamName, static (dict, oldKey) => dict.Remove(oldKey), oldNameKey);
        ImmutableInterlocked.Update(ref _byTeamName, static (dict, arg) => dict.SetItem(arg.nameKey, arg.teamId), (nameKey: newNameKey!, teamId));
    }

    /// <summary>
    /// 移除团队房间,同时清理 agent 映射 + 冗余查询索引,保证无孤儿映射。
    /// </summary>
    public bool TryRemoveRoom(string teamId, [MaybeNullWhen(false)] out ChatRoomState removedRoom) {
        var snapshot = _rooms;
        if (!snapshot.TryGetValue(teamId, out removedRoom)) return false;
        ImmutableInterlocked.Update(ref _rooms, static (dict, id) => dict.Remove(id), teamId);
        ImmutableInterlocked.Update(ref _bySessionId, static (dict, sid) => dict.Remove(sid), removedRoom.SessionId!);
        ImmutableInterlocked.Update(ref _byTeamName, static (dict, nameKey) => dict.Remove(nameKey), removedRoom.Info.TeamName.ToLowerInvariant()!);
        foreach (var member in removedRoom.Members) {
            ImmutableInterlocked.Update(ref _agentToTeam, static (dict, m) => dict.Remove(m), member);
        }
        return true;
    }

    /// <summary>
    /// 注册 agent→team 映射。
    /// </summary>
    public void RegisterAgentToTeam(string agentId, string teamId)
        => ImmutableInterlocked.Update(ref _agentToTeam, static (dict, arg) => dict.SetItem(arg.agentId, arg.teamId), (agentId, teamId));

    /// <summary>
    /// 注销 agent→team 映射。
    /// </summary>
    public bool UnregisterAgentFromTeam(string agentId) {
        var existed = _agentToTeam.ContainsKey(agentId);
        if (existed) ImmutableInterlocked.Update(ref _agentToTeam, static (dict, id) => dict.Remove(id), agentId);
        return existed;
    }

    /// <summary>
    /// 按 sessionId 查找团队房间 — O(1) 冗余索引查找 + 一致性验证。
    /// </summary>
    public ChatRoomState? FindRoomBySessionId(string sessionId) {
        if (_bySessionId.TryGetValue(sessionId, out var teamId)
            && _rooms.TryGetValue(teamId, out var room)
            && room.SessionId == sessionId)
            return room;
        return null;
    }

    /// <summary>
    /// 按团队名称查找团队信息 — O(1) 冗余索引查找(忽略大小写)。
    /// </summary>
    public TeamInfo? FindTeamByName(string teamName) {
        var nameKey = teamName.ToLowerInvariant();
        if (_byTeamName.TryGetValue(nameKey, out var teamId)
            && _rooms.TryGetValue(teamId, out var room))
            return room.Info;
        return null;
    }
}
