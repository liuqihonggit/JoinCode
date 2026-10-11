namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 定时任务实体 — 派生自 Entity，与 Agent 同套路
/// 代表运行时定时任务（区别于 CronTask record，后者是持久化层 DTO）
/// </summary>
public sealed class CronTaskEntity : Entity {
    /// <summary>获取 Cron 表达式。</summary>
    public string CronExpression { get; }
    /// <summary>获取触发提示词。</summary>
    public string Prompt { get; }
    /// <summary>获取是否为重复任务。</summary>
    public bool IsRecurring { get; init; }
    /// <summary>获取是否为永久任务。</summary>
    public bool IsPermanent { get; init; }
    /// <summary>获取是否持久化。</summary>
    public bool IsDurable { get; init; } = true;
    /// <summary>获取关联代理标识。</summary>
    public ObjectId? AgentObjectId { get; init; }
    /// <summary>获取或设置最近触发时间戳。</summary>
    public long? LastFiredAt { get; set; }

    /// <summary>
    /// 全局唯一 CronTask 注册器
    /// </summary>
    public static CronTaskEntityRegistry Registry { get; } = new();

    /// <summary>构造定时任务实体。</summary>
    public CronTaskEntity(
        string cronExpression,
        string prompt,
        bool isRecurring = false,
        bool isPermanent = false,
        bool isDurable = true,
        ObjectId? agentObjectId = default,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(ObjectType.Cron, sessionId, displayName ?? prompt) {
        CronExpression = cronExpression;
        Prompt = prompt;
        IsRecurring = isRecurring;
        IsPermanent = isPermanent;
        IsDurable = isDurable;
        AgentObjectId = agentObjectId;
        Registry.Add(ObjectId, this);
    }

    /// <summary>释放资源。</summary>
    public override void Dispose() {
        Registry.Remove(ObjectId);
        base.Dispose();
    }

    /// <summary>判断任务是否已过期。</summary>
    public bool IsExpired(long nowMs, long maxAgeMs) {
        if (maxAgeMs == 0 || IsPermanent) return false;
        var createdAtMs = new DateTimeOffset(CreatedAt).ToUnixTimeMilliseconds();
        return IsRecurring && (nowMs - createdAtMs) >= maxAgeMs;
    }
}

/// <summary>
/// CronTask 注册器 — 基于 MapRegistry
/// </summary>
public sealed class CronTaskEntityRegistry : MapRegistry<ObjectId, CronTaskEntity> {
    internal void Add(ObjectId id, CronTaskEntity task) => AddCore(id, task);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    /// <summary>获取所有活动任务。</summary>
    public IEnumerable<CronTaskEntity> GetActive() => Where(t => t.LastFiredAt.HasValue);
}