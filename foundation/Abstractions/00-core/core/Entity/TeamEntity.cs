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
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// 全局唯一 Team 注册器
    /// </summary>
    public static TeamEntityRegistry Registry { get; } = new();

    public TeamEntity(
        string teamName,
        string? description = null,
        ObjectId? leadAgentObjectId = default,
        string? id = null)
        : base(ObjectType.Team, id)
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
        TeamId = Id,
        TeamName = TeamName,
        Description = Description,
        LeadAgentId = LeadAgentObjectId?.Id,
        Members = Members,
        CreatedAt = CreatedAt,
        LastActivityAt = LastActivityAt
    };
}

/// <summary>
/// Team 注册器
/// </summary>
public sealed class TeamEntityRegistry
{
    private readonly ConcurrentDictionary<ObjectId, TeamEntity> _teams = new();

    internal void Add(ObjectId id, TeamEntity team) => _teams.TryAdd(id, team);
    internal bool Remove(ObjectId id) => _teams.TryRemove(id, out _);
    public TeamEntity? Get(ObjectId id) => _teams.GetValueOrDefault(id);
    public IReadOnlyList<TeamEntity> GetAll() => [.. _teams.Values];
    public int Count => _teams.Count;
    public void Clear() => _teams.Clear();
}
