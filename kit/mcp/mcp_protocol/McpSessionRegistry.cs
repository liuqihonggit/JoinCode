namespace McpProtocol;

/// <summary>
/// MCP 会话注册表 — 封装 sessionId→创建时间的映射,消除 McpTcpServer + McpHttpServer 重复。
/// <para>用于有状态模式验证会话存在性、注册新会话、移除会话、查询活跃数。</para>
/// <para>无锁 CAS 更新，不可变快照读取。</para>
/// </summary>
internal sealed class McpSessionRegistry {
    private ImmutableDictionary<string, DateTime> _sessions = ImmutableDictionary<string, DateTime>.Empty;

    /// <summary>活跃会话数</summary>
    public int ActiveSessionCount => _sessions.Count;

    /// <summary>会话是否存在</summary>
    public bool Contains(string sessionId) => _sessions.ContainsKey(sessionId);

    /// <summary>注册新会话(记录创建时间)</summary>
    public void Register(string sessionId) {
        var now = DateTime.UtcNow;
        while (true) {
            var current = _sessions;
            var updated = current.SetItem(sessionId, now);
            if (Interlocked.CompareExchange(ref _sessions, updated, current) == current) return;
        }
    }

    /// <summary>移除会话</summary>
    public void Remove(string sessionId) {
        while (true) {
            var current = _sessions;
            if (!current.ContainsKey(sessionId)) return;
            var updated = current.Remove(sessionId);
            if (Interlocked.CompareExchange(ref _sessions, updated, current) == current) return;
        }
    }
}
