namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 通道信息 — 合并通道偏好 + 聊天室角色为单一不可变记录
/// </summary>
internal sealed record AgentChannelInfo
{
    /// <summary>通道类型</summary>
    public required MailboxKind Channel { get; init; }

    /// <summary>聊天室角色（用于 AdminOnly 可见性过滤）</summary>
    public ChatRoomRole Role { get; init; } = ChatRoomRole.Member;
}

/// <summary>
/// Agent 通道注册表 — 管理 agent 的通道偏好和聊天室角色
/// 持有以 agentId 为 key 的合并字典，提供注册、注销、查询、遍历操作
/// </summary>
internal sealed class AgentChannelRegistry
{
    private readonly ConcurrentDictionary<string, AgentChannelInfo> _entries = new();

    // ── 查询 ──

    /// <summary>获取 agent 的通道类型（未注册返回 InProcess）</summary>
    public MailboxKind GetChannel(string agentId)
        => _entries.GetValueOrDefault(agentId)?.Channel ?? MailboxKind.InProcess;

    /// <summary>获取 agent 的聊天室角色（未注册返回 Member）</summary>
    public ChatRoomRole GetRole(string agentId)
        => _entries.GetValueOrDefault(agentId)?.Role ?? ChatRoomRole.Member;

    /// <summary>获取所有 agent 的角色映射（用于 AdminOnly 广播过滤）</summary>
    public IEnumerable<KeyValuePair<string, ChatRoomRole>> GetAllRoles()
        => _entries.Select(kvp => new KeyValuePair<string, ChatRoomRole>(kvp.Key, kvp.Value.Role));

    // ── 修改 ──

    /// <summary>注册 agent 通道和角色（同时设置，覆盖已有记录）</summary>
    public void Register(string agentId, MailboxKind channel, ChatRoomRole role)
        => _entries[agentId] = new AgentChannelInfo { Channel = channel, Role = role };

    /// <summary>仅设置通道类型，保留已有角色（未注册则用默认 Member）</summary>
    public void SetChannel(string agentId, MailboxKind channel)
        => _entries.AddOrUpdate(
            agentId,
            _ => new AgentChannelInfo { Channel = channel },
            (_, old) => old with { Channel = channel });

    /// <summary>注销 agent（返回是否找到，并输出通道类型用于后续清理）</summary>
    public bool Unregister(string agentId, out MailboxKind channel)
    {
        if (_entries.TryRemove(agentId, out var info))
        {
            channel = info.Channel;
            return true;
        }
        channel = MailboxKind.InProcess;
        return false;
    }
}
