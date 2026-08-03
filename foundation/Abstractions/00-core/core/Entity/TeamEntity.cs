namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 团队实体 — 派生自 Entity，与 Agent 同套路
/// 代表运行时团队（区别于 TeamInfo record，后者是数据模型 DTO）
/// </summary>
public sealed class TeamEntity : Entity
{
    public string TeamName { get; }
    public string? Description { get; init; }
    public ObjectId? LeadAgentObjectId { get; init; }
    public List<string> Members { get; init; } = [];

    /// <summary>
    /// 全局唯一 Team 注册器
    /// </summary>
    public static TeamEntityRegistry Registry { get; } = new();

    public TeamEntity(
        string teamName,
        string? description = null,
        ObjectId? leadAgentObjectId = default,
        string? displayName = null)
        : base(ObjectType.Team, displayName ?? teamName)
    {
        TeamName = teamName;
        Description = description;
        LeadAgentObjectId = leadAgentObjectId;
        LastActivityAt = DateTime.UtcNow;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose()
    {
        Registry.Remove(ObjectId);
    }

    public TeamInfo ToTeamInfo() => new()
    {
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
public sealed class TeamEntityRegistry : MapRegistry<ObjectId, TeamEntity>
{
    internal void Add(ObjectId id, TeamEntity team) => AddCore(id, team);
    internal bool Remove(ObjectId id) => RemoveCore(id);
}
