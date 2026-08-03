namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 定时任务实体 — 派生自 Entity，与 Agent 同套路
/// 代表运行时定时任务（区别于 CronTask record，后者是持久化层 DTO）
/// </summary>
public sealed class CronTaskEntity : Entity
{
    public string CronExpression { get; }
    public string Prompt { get; }
    public bool IsRecurring { get; init; }
    public bool IsPermanent { get; init; }
    public bool IsDurable { get; init; } = true;
    public ObjectId? AgentObjectId { get; init; }
    public long? LastFiredAt { get; set; }

    /// <summary>
    /// 全局唯一 CronTask 注册器
    /// </summary>
    public static CronTaskEntityRegistry Registry { get; } = new();

    public CronTaskEntity(
        string cronExpression,
        string prompt,
        bool isRecurring = false,
        bool isPermanent = false,
        bool isDurable = true,
        ObjectId? agentObjectId = default,
        string? displayName = null)
        : base(ObjectType.Cron, displayName ?? prompt)
    {
        CronExpression = cronExpression;
        Prompt = prompt;
        IsRecurring = isRecurring;
        IsPermanent = isPermanent;
        IsDurable = isDurable;
        AgentObjectId = agentObjectId;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose()
    {
        Registry.Remove(ObjectId);
    }

    public bool IsExpired(long nowMs, long maxAgeMs)
    {
        if (maxAgeMs == 0 || IsPermanent) return false;
        var createdAtMs = new DateTimeOffset(CreatedAt).ToUnixTimeMilliseconds();
        return IsRecurring && (nowMs - createdAtMs) >= maxAgeMs;
    }
}

/// <summary>
/// CronTask 注册器 — 基于 MapRegistry
/// </summary>
public sealed class CronTaskEntityRegistry : MapRegistry<ObjectId, CronTaskEntity>
{
    internal void Add(ObjectId id, CronTaskEntity task) => AddCore(id, task);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    public IEnumerable<CronTaskEntity> GetActive() => Where(t => t.LastFiredAt.HasValue);
}
