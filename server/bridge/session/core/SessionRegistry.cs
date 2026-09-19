namespace Core.Bridge;

/// <summary>
/// 会话注册表 — 合并 _sessions + _clientIdToSessionId 两个字典
/// 从 BridgeSessionRunner 提取,降低大类字段数和会话管理逻辑复杂度
/// _sessions: sessionId → BridgeSession
/// _clientIdToSessionId: clientId → sessionId
/// 创建时同时写入,关闭时只移除 client 映射,清理时才移除 session
/// </summary>
internal sealed class SessionRegistry {
    private readonly ConcurrentDictionary<string, BridgeSession> _sessions = new();
    private readonly ConcurrentDictionary<string, string> _clientIdToSessionId = new();

    /// <summary>添加会话 — 同时写入 session 和 client 映射</summary>
    public void Add(BridgeSession session) {
        _sessions[session.SessionId] = session;
        _clientIdToSessionId[session.ClientId] = session.SessionId;
    }

    /// <summary>尝试获取会话 by sessionId</summary>
    public bool TryGet(string sessionId, [MaybeNullWhen(false)] out BridgeSession session)
        => _sessions.TryGetValue(sessionId, out session);

    /// <summary>根据 clientId 获取会话</summary>
    public BridgeSession? GetByClientId(string clientId)
        => _clientIdToSessionId.TryGetValue(clientId, out var sessionId)
            ? (_sessions.TryGetValue(sessionId, out var session) ? session : null)
            : null;

    /// <summary>移除 client 映射（会话关闭时调用,session 保留在字典中等待清理）</summary>
    public void RemoveClientMapping(string clientId)
        => _clientIdToSessionId.TryRemove(clientId, out _);

    /// <summary>检查 clientId 映射的 sessionId 是否匹配</summary>
    public bool IsClientMappedTo(string clientId, string sessionId)
        => _clientIdToSessionId.TryGetValue(clientId, out var mappedId) && mappedId == sessionId;

    /// <summary>移除会话（清理时调用）</summary>
    public bool Remove(string sessionId)
        => _sessions.TryRemove(sessionId, out _);

    /// <summary>所有会话</summary>
    public IEnumerable<BridgeSession> Values => _sessions.Values;

    /// <summary>活跃会话数量</summary>
    public int CountActive()
        => _sessions.Values.Count(s => s.Status == BridgeSessionStatus.Active);
}