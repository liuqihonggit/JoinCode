namespace McpProtocol;

/// <summary>
/// MCP 会话注册表 — 封装 sessionId→创建时间的映射,消除 McpTcpServer + McpHttpServer 重复。
/// <para>用于有状态模式验证会话存在性、注册新会话、移除会话、查询活跃数。</para>
/// </summary>
internal sealed class McpSessionRegistry {
    private readonly ConcurrentDictionary<string, DateTime> _sessions = new(StringComparer.Ordinal);

    /// <summary>活跃会话数</summary>
    public int ActiveSessionCount => _sessions.Count;

    /// <summary>会话是否存在</summary>
    public bool Contains(string sessionId) => _sessions.ContainsKey(sessionId);

    /// <summary>注册新会话(记录创建时间)</summary>
    public void Register(string sessionId) => _sessions[sessionId] = DateTime.UtcNow;

    /// <summary>移除会话</summary>
    public void Remove(string sessionId) => _sessions.TryRemove(sessionId, out _);
}