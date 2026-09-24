namespace JoinCode.Abstractions.Entity;

/// <summary>
/// MCP服务器实体 — 派生自 Entity，与 Agent 同套路
/// 代表运行时MCP服务器连接（区别于 McpServerState record，后者是 UI 状态层 DTO）
/// </summary>
public sealed class McpServerEntity : Entity {
    /// <summary>获取服务器名称。</summary>
    public string Name { get; }
    /// <summary>获取或设置连接状态。</summary>
    public McpConnectionStatus Status { get; set; } = McpConnectionStatus.Disconnected;
    /// <summary>获取或设置最后错误信息。</summary>
    public string? LastError { get; set; }
    /// <summary>获取或设置连接时间。</summary>
    public DateTime? ConnectedAt { get; set; }

    /// <summary>
    /// 全局唯一 McpServer 注册器
    /// </summary>
    public static McpServerEntityRegistry Registry { get; } = new();

    /// <summary>构造 MCP 服务器实体。</summary>
    /// <param name="name">服务器名称。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="sessionId">会话标识。</param>
    public McpServerEntity(
        string name,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(ObjectType.Mcp, sessionId, displayName ?? name) {
        Name = name;
        Registry.Add(ObjectId, this);
    }

    /// <summary>释放资源。</summary>
    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }

    /// <summary>转换为 MCP 服务器状态 DTO。</summary>
    public McpServerState ToMcpServerState() => new() {
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
public sealed class McpServerEntityRegistry : MapRegistry<ObjectId, McpServerEntity> {
    private readonly SecondaryIndex<ObjectId, McpServerEntity, McpConnectionStatus> _byStatus;

    /// <summary>构造 McpServerEntityRegistry，初始化次级索引</summary>
    public McpServerEntityRegistry() {
        _byStatus = CreateIndex(s => s.Status);
    }

    internal void Add(ObjectId id, McpServerEntity server) => AddCore(id, server);
    internal bool Remove(ObjectId id) => RemoveCore(id);

    /// <summary>状态转换 — 更新 McpServerEntity.Status 并同步次级索引</summary>
    public void TransitionStatus(ObjectId id, McpConnectionStatus newState) {
        var entity = Get(id);
        if (entity is null) return;
        var oldState = entity.Status;
        if (oldState == newState) return;
        entity.Status = newState;
        Reindex(_byStatus, id, oldState, newState);
    }

    /// <summary>按连接状态获取 MCP 服务器实体集合（O(1) 索引查找）。</summary>
    public IEnumerable<McpServerEntity> GetByStatus(McpConnectionStatus status) => _byStatus.GetValues(status, AsDictionary());
}