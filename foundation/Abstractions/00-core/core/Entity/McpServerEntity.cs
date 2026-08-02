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
        string? id = null)
        : base(ObjectType.Mcp, id)
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
        ServerId = Id,
        Status = Status,
        LastError = LastError,
        ConnectedAt = ConnectedAt
    };
}

/// <summary>
/// McpServer 注册器
/// </summary>
public sealed class McpServerEntityRegistry
{
    private readonly ConcurrentDictionary<ObjectId, McpServerEntity> _servers = new();

    internal void Add(ObjectId id, McpServerEntity server) => _servers.TryAdd(id, server);
    internal bool Remove(ObjectId id) => _servers.TryRemove(id, out _);
    public McpServerEntity? Get(ObjectId id) => _servers.GetValueOrDefault(id);
    public IReadOnlyList<McpServerEntity> GetAll() => [.. _servers.Values];
    public IReadOnlyList<McpServerEntity> GetByStatus(McpConnectionStatus status) => [.. _servers.Values.Where(s => s.Status == status)];
    public int Count => _servers.Count;
    public void Clear() => _servers.Clear();
}
