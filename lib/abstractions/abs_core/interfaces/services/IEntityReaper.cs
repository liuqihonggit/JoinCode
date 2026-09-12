namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 实体回收器接口 — 定期扫描 ObjectIdManager 中可回收的 Entity
/// 判定逻辑：entity.CanReclaim() == true → Dispose()
/// 超时检测：entity.IsTimedOut → 触发 EntityTimeout 事件
/// 泄漏检测：超龄未 Dispose 的 Entity → 告警
/// </summary>
public interface IEntityReaper
{
    /// <summary>
    /// 执行一次扫描 — 遍历所有 Entity，回收/超时检测/泄漏检测
    /// </summary>
    /// <returns>本次回收的 Entity 数量</returns>
    int ScanOnce();

    /// <summary>
    /// 获取当前疑似泄漏的 Entity 列表
    /// </summary>
    IReadOnlyList<Entity.Entity> GetLeakedEntities();

    /// <summary>
    /// 获取当前超时的 Entity 列表
    /// </summary>
    IReadOnlyList<Entity.Entity> GetTimedOutEntities();

    /// <summary>
    /// Entity 被回收事件
    /// </summary>
    event EventHandler<Entity.Entity>? EntityReclaimed;

    /// <summary>
    /// Entity 超时事件
    /// </summary>
    event EventHandler<Entity.Entity>? EntityTimeout;

    /// <summary>
    /// Entity 疑似泄漏事件
    /// </summary>
    event EventHandler<Entity.Entity>? EntityLeakDetected;
}
