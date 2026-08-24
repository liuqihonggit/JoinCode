namespace Infrastructure.EntityReaper;

/// <summary>
/// 实体回收器 — 定期扫描 ObjectIdManager 中可回收/超时/泄漏的 Entity
/// 独立于 HousekeepingService（文件清理），专注于内存中 Entity 的生命周期管理
/// </summary>
[Register(typeof(IEntityReaper), ServiceLifetime.Singleton)]
public sealed partial class EntityReaper : IEntityReaper, IScanStrategy
{
    private readonly IClockService _clock;
    private readonly ILogger<EntityReaper>? _logger;

    private readonly EntityReaperConfig _config;

    public EntityReaper(IClockService clock, EntityReaperConfig? config = null, ILogger<EntityReaper>? logger = null)
    {
        _clock = clock;
        _config = config ?? new EntityReaperConfig();
        _logger = logger;
    }

    public int ScanOnce()
    {
        var reclaimedCount = 0;
        var now = _clock.GetUtcNow();

        var allObjects = ObjectIdManager.Count > 0
            ? GetAllEntities()
            : [];

        foreach (var entity in allObjects)
        {
            if (entity.LifecycleState == EntityLifecycle.Disposed)
                continue;

            if (entity.IsTimedOut)
            {
                OnEntityTimeout(entity);
            }

            if (_config.EnableLeakDetection && IsLeaked(entity, now))
            {
                OnEntityLeakDetected(entity);
            }

            if (_config.EnableAutoReclaim && entity.CanReclaim())
            {
                try
                {
                    entity.Dispose();
                    reclaimedCount++;
                    OnEntityReclaimed(entity);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "回收 Entity {ObjectId} 失败", entity.ObjectId);
                }
            }
        }

        if (reclaimedCount > 0)
        {
            _logger?.LogDebug("EntityReaper 扫描完成: 回收 {Count} 个 Entity", reclaimedCount);
        }

        return reclaimedCount;
    }

    public IReadOnlyList<JoinCode.Abstractions.Entity.Entity> GetLeakedEntities()
    {
        var now = _clock.GetUtcNow();
        return GetAllEntities()
            .Where(e => e.LifecycleState != EntityLifecycle.Disposed && IsLeaked(e, now))
            .ToList();
    }

    public IReadOnlyList<JoinCode.Abstractions.Entity.Entity> GetTimedOutEntities()
    {
        return GetAllEntities()
            .Where(e => e.LifecycleState != EntityLifecycle.Disposed && e.IsTimedOut)
            .ToList();
    }

    public event EventHandler<JoinCode.Abstractions.Entity.Entity>? EntityReclaimed;
    public event EventHandler<JoinCode.Abstractions.Entity.Entity>? EntityTimeout;
    public event EventHandler<JoinCode.Abstractions.Entity.Entity>? EntityLeakDetected;

    private bool IsLeaked(JoinCode.Abstractions.Entity.Entity entity, DateTime now)
    {
        return now - entity.LastActivityAt > _config.MaxAgeBeforeLeakWarning
            && entity.LifecycleState is not (EntityLifecycle.Persisted or EntityLifecycle.Disposed);
    }

    private IReadOnlyList<JoinCode.Abstractions.Entity.Entity> GetAllEntities()
    {
        return ObjectIdManager.GetAll<JoinCode.Abstractions.Entity.Entity>();
    }

    private void OnEntityReclaimed(JoinCode.Abstractions.Entity.Entity entity)
    {
        _logger?.LogDebug("Entity 已回收: {ObjectId} ({DisplayName})", entity.ObjectId, entity.DisplayName);
        EntityReclaimed?.Invoke(this, entity);
    }

    private void OnEntityTimeout(JoinCode.Abstractions.Entity.Entity entity)
    {
        _logger?.LogWarning("Entity 超时: {ObjectId} ({DisplayName}), TimeoutAt={TimeoutAt}", entity.ObjectId, entity.DisplayName, entity.TimeoutAt);
        EntityTimeoutCausalMark(entity);
        EntityTimeout?.Invoke(this, entity);
    }

    private void OnEntityLeakDetected(JoinCode.Abstractions.Entity.Entity entity)
    {
        _logger?.LogWarning("Entity 疑似泄漏: {ObjectId} ({DisplayName}), CreatedAt={CreatedAt}, LastActivityAt={LastActivityAt}", entity.ObjectId, entity.DisplayName, entity.CreatedAt, entity.LastActivityAt);
        EntityLeakDetected?.Invoke(this, entity);
    }

    /// <summary>
    /// 超时因果标记 — 在 Entity 上设置 CompletedAt 记录超时时刻，使 CanReclaim 条件满足
    /// </summary>
    private static void EntityTimeoutCausalMark(JoinCode.Abstractions.Entity.Entity entity)
    {
        if (!entity.CompletedAt.HasValue)
        {
            entity.CompletedAt = entity.TimeoutAt;
        }
    }

    /// <summary>IScanStrategy.Name</summary>
    public string Name => "EntityReaper";

    /// <summary>
    /// IScanStrategy.Scan — 按会话隔离扫描, 只扫描该会话的 Entity
    /// </summary>
    public void Scan(SessionScope scope)
    {
        var now = _clock.GetUtcNow();
        foreach (var entity in scope.GetAll())
        {
            if (entity.LifecycleState == EntityLifecycle.Disposed)
                continue;

            if (entity.IsTimedOut)
                OnEntityTimeout(entity);

            if (_config.EnableLeakDetection && IsLeaked(entity, now))
                OnEntityLeakDetected(entity);

            if (_config.EnableAutoReclaim && entity.CanReclaim())
            {
                try
                {
                    entity.Dispose();
                    OnEntityReclaimed(entity);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "回收 Entity {ObjectId} 失败", entity.ObjectId);
                }
            }
        }
    }
}
