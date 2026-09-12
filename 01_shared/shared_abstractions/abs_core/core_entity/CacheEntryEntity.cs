namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 缓存项实体 — 泛型 CacheEntryEntity&lt;T&gt;, Value 类型安全, AOT 友好
/// 派生自 Entity, 纳入 EntityReaper 回收体系, 会话级隔离
/// 过期或会话结束时自动 Dispose
/// </summary>
public sealed class CacheEntryEntity<T> : Entity
{
    /// <summary>缓存键</summary>
    public string CacheKey { get; }

    /// <summary>缓存值 — 泛型, AOT 友好无 trim 风险</summary>
    public T? Value { get; set; }

    /// <summary>过期时刻 — null 表示永不过期</summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>命中次数 — 诊断用</summary>
    public long HitCount { get; set; }

    /// <summary>估算大小(字节) — 诊断用, 0 表示未统计</summary>
    public long SizeBytes { get; set; }

    /// <summary>是否已过期</summary>
    public bool IsExpired => ExpiresAt.HasValue && DateTime.UtcNow > ExpiresAt.Value;

    public static CacheEntryEntityRegistry Registry { get; } = new();

    public CacheEntryEntity(
        string cacheKey,
        T? value = default,
        TimeSpan? ttl = null,
        long sizeBytes = 0,
        string? displayName = null,
        ObjectId sessionId = default)
        : base(ObjectType.Cache, sessionId, displayName ?? cacheKey)
    {
        CacheKey = cacheKey;
        Value = value;
        ExpiresAt = ttl.HasValue ? DateTime.UtcNow + ttl.Value : null;
        SizeBytes = sizeBytes;
        Registry.Add(ObjectId, this);
    }

    protected override void OnDispose() => Registry.Remove(ObjectId);

    /// <summary>
    /// 回收判定 — 已过期 或 已持久化
    /// </summary>
    public override bool CanReclaim()
        => IsExpired || base.CanReclaim();

    /// <summary>命中 — 计数+1, 刷新活跃时刻</summary>
    public void OnHit()
    {
        HitCount++;
        Touch();
    }
}

/// <summary>
/// 缓存项全局注册器 — EntityReaper 统一扫描回收
/// </summary>
public sealed class CacheEntryEntityRegistry : MapRegistry<ObjectId, Entity>
{
    internal void Add(ObjectId id, Entity entry) => AddCore(id, entry);
    internal bool Remove(ObjectId id) => RemoveCore(id);
    public IEnumerable<Entity> GetExpired() => Where(e => e is CacheEntryEntity<object> c && c.IsExpired);
}
