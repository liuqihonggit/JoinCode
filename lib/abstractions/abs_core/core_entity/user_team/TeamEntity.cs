namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 团队实体 — 派生自 Entity，与 Agent 同套路
/// 代表运行时团队（区别于 TeamInfo record，后者是数据模型 DTO）
/// </summary>
public sealed class TeamEntity : Entity {
    /// <summary>获取团队名称。</summary>
    public string TeamName { get; }
    /// <summary>获取或设置团队描述。</summary>
    public string? Description { get; init; }
    /// <summary>获取或设置队长代理对象 ID。</summary>
    public ObjectId? LeadAgentObjectId { get; init; }
    /// <summary>获取或设置成员列表。</summary>
    public List<string> Members { get; init; } = [];

    /// <summary>
    /// 全局唯一 Team 注册器
    /// </summary>
    public static TeamEntityRegistry Registry { get; } = new();

    /// <summary>
    /// 构造团队实体。
    /// </summary>
    /// <param name="teamName">团队名称。</param>
    /// <param name="description">团队描述。</param>
    /// <param name="leadAgentObjectId">队长代理对象 ID。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="sessionId">会话 ID。</param>
    public TeamEntity(
        string teamName,
        string? description = null,
        ObjectId? leadAgentObjectId = default,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(ObjectType.Team, sessionId, displayName ?? teamName) {
        TeamName = teamName;
        Description = description;
        LeadAgentObjectId = leadAgentObjectId;
        LastActivityAt = DateTime.UtcNow;
        Registry.Add(ObjectId, this);
    }

    /// <summary>释放资源。</summary>
    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }

    /// <summary>转换为团队信息 DTO。</summary>
    public TeamInfo ToTeamInfo() => new() {
        TeamId = UniqueId,
        TeamName = TeamName,
        Description = Description,
        LeadAgentId = LeadAgentObjectId?.SequenceId.ToString(CultureInfo.InvariantCulture),
        Members = Members,
        CreatedAt = CreatedAt,
        LastActivityAt = LastActivityAt
    };
}

/// <summary>
/// Team 注册器 — 基于 MapRegistry
/// </summary>
public sealed class TeamEntityRegistry : MapRegistry<ObjectId, TeamEntity> {
    internal void Add(ObjectId id, TeamEntity team) => AddCore(id, team);
    internal bool Remove(ObjectId id) => RemoveCore(id);
}
