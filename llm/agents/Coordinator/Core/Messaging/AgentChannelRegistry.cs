namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 通道信息 — 合并通道偏好 + 聊天室角色为单一不可变记录
/// </summary>
internal sealed record AgentChannelInfo {
    /// <summary>通道类型</summary>
    public required MailboxKind Channel { get; init; }

    /// <summary>聊天室角色（用于 AdminOnly 可见性过滤）</summary>
    public ChatRoomRole Role { get; init; } = ChatRoomRole.Member;
}

/// <summary>
/// Agent 通道注册表 — 管理 agent 的通道偏好和聊天室角色
/// 持有以 agentId 为 key 的合并字典，提供注册、注销、查询、遍历操作
/// </summary>
internal sealed class AgentChannelRegistry {
    private volatile ImmutableDictionary<string, AgentChannelInfo> _entries = ImmutableDictionary<string, AgentChannelInfo>.Empty;

    // ── 查询 ──

    /// <summary>获取 agent 的通道类型（未注册返回 InProcess）</summary>
    public MailboxKind GetChannel(string agentId)
        => _entries.TryGetValue(agentId, out var info) ? info.Channel : MailboxKind.InProcess;

    /// <summary>获取 agent 的聊天室角色（未注册返回 Member）</summary>
    public ChatRoomRole GetRole(string agentId)
        => _entries.TryGetValue(agentId, out var info) ? info.Role : ChatRoomRole.Member;

    /// <summary>获取所有 agent 的角色映射（用于 AdminOnly 广播过滤）</summary>
    public IEnumerable<KeyValuePair<string, ChatRoomRole>> GetAllRoles()
        => _entries.Select(kvp => new KeyValuePair<string, ChatRoomRole>(kvp.Key, kvp.Value.Role));

    // ── 修改 ──

    /// <summary>注册 agent 通道和角色（同时设置，覆盖已有记录）</summary>
    public void Register(string agentId, MailboxKind channel, ChatRoomRole role)
        => SetEntry(agentId, new AgentChannelInfo { Channel = channel, Role = role });

    /// <summary>仅设置通道类型，保留已有角色（未注册则用默认 Member）</summary>
    public void SetChannel(string agentId, MailboxKind channel)
        => SetChannelEntry(agentId, channel);

    /// <summary>注销 agent（返回是否找到，并输出通道类型用于后续清理）</summary>
    public bool Unregister(string agentId, out MailboxKind channel) {
        if (TryRemoveEntry(agentId, out var info)) {
            channel = info.Channel;
            return true;
        }
        channel = MailboxKind.InProcess;
        return false;
    }

    private void SetEntry(string key, AgentChannelInfo value) {
        var current = _entries;
        while (true) {
            var updated = current.SetItem(key, value);
            if (Interlocked.CompareExchange(ref _entries, updated, current) == current) return;
            current = _entries;
        }
    }

    private void SetChannelEntry(string agentId, MailboxKind channel) {
        var current = _entries;
        while (true) {
            var updated = current.TryGetValue(agentId, out var existing)
                ? current.SetItem(agentId, existing with { Channel = channel })
                : current.Add(agentId, new AgentChannelInfo { Channel = channel });
            if (Interlocked.CompareExchange(ref _entries, updated, current) == current) return;
            current = _entries;
        }
    }

    private bool TryRemoveEntry(string key, out AgentChannelInfo value) {
        value = null!;
        var current = _entries;
        while (current.ContainsKey(key)) {
            value = current[key];
            var updated = current.Remove(key);
            if (Interlocked.CompareExchange(ref _entries, updated, current) == current) return true;
            current = _entries;
        }
        return false;
    }
}