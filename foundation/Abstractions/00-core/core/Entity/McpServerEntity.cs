namespace JoinCode.Abstractions.Entity;

/// <summary>
/// MCP服务器实体 — 派生自 Entity，与 Agent 同套路
/// 代表运行时MCP服务器连接（区别于 McpServerState record，后者是 UI 状态层 DTO）
/// </summary>
public sealed class McpServerEntity : Entity
{
    public string Name { get; }
    public McpConnectionStatus Status { get; set; } = McpConnectionStatus.Disconnected;
    public string? LastError { get; set; }
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// 全局唯一 McpServer 注册器
    /// </summary>
    public static McpServerEntityRegistry Registry { get; } = new();

    public McpServerEntity(
        string name,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(ObjectType.Mcp, sessionId, displayName ?? name)
    {
        Name = name;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose()
    {
        Registry.Remove(ObjectId);
    }

    public McpServerState ToMcpServerState() => new()
    {
        Name = Name,
        ServerId = UniqueId,
        Status = Status,
        LastError = LastError,
        ConnectedAt = ConnectedAt
    };
}

/// <summary>
/// McpServer 注册器 — 基于 MapRegistry
/// </summary>
public sealed class McpServerEntityRegistry : MapRegistry<ObjectId, McpServerEntity>
{
    internal void Add(ObjectId id, McpServerEntity server) => AddCore(id, server);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    public IEnumerable<McpServerEntity> GetByStatus(McpConnectionStatus status) => Where(s => s.Status == status);
}
