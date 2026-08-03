namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 实体基类 — 所有有生命周期的实体派生此类
/// 共同属性：ObjectId + CreatedAt + StartedAt + CompletedAt + LifecycleState + 惰性释放 + 回收判定
/// 加一个共同属性只改此处，不需要改所有子类
/// </summary>
public abstract class Entity : IDisposable
{
    public ObjectId ObjectId { get; }
    public long Id => ObjectId.SequenceId;
    public string UniqueId => ObjectId.UniqueId;
    public string DisplayName => ObjectId.DisplayName;
    public DateTime CreatedAt { get; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 生命周期状态 — Created → Active → Suspended → Completed → Persisted → Disposed
    /// </summary>
    public EntityLifecycle LifecycleState { get; set; } = EntityLifecycle.Created;

    /// <summary>
    /// 超时时刻 — 超过此时刻未完成则触发超时事件，null 表示不超时
    /// </summary>
    public DateTime? TimeoutAt { get; init; }

    /// <summary>
    /// 最后活跃时刻 — 用于超时判定和泄漏检测，每次 Touch() 刷新
    /// </summary>
    public DateTime LastActivityAt { get; set; }

    /// <summary>
    /// 是否已持久化 — 回收前提条件，MarkPersisted() 设置
    /// </summary>
    public bool IsPersisted { get; private set; }

    /// <summary>
    /// 关联的链路追踪ID — 构造时自动捕获 Activity.Current?.TraceId
    /// </summary>
    public string? TraceId { get; }

    private bool _disposed;

    protected Entity(ObjectType type, string? displayName = null)
    {
        ObjectId = new ObjectId(type, displayName);
        CreatedAt = DateTime.UtcNow;
        LastActivityAt = CreatedAt;
        TraceId = System.Diagnostics.Activity.Current?.TraceId.ToString();
        ObjectIdManager.Register(this, ObjectId);
    }

    /// <summary>
    /// 惰性释放 — 只有持久化服务确认消息全部写入后才调用
    /// 任务完成不释放，持久化完消息后才 Dispose
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        LifecycleState = EntityLifecycle.Disposed;
        OnDispose();
        ObjectIdManager.Unregister(ObjectId);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 子类覆写：从各自 Registry 移除、释放资源等
    /// </summary>
    protected abstract void OnDispose();

    /// <summary>
    /// 回收判定 — 默认: LifecycleState==Persisted 且 CompletedAt!=null
    /// 子类可覆写增加额外条件（如 Agent 检查 Status!=Running）
    /// </summary>
    public virtual bool CanReclaim()
    {
        return LifecycleState == EntityLifecycle.Persisted && CompletedAt.HasValue;
    }

    /// <summary>
    /// 标记已持久化 — 持久化服务确认数据全部写入后调用
    /// </summary>
    public void MarkPersisted()
    {
        IsPersisted = true;
        if (LifecycleState == EntityLifecycle.Completed)
        {
            LifecycleState = EntityLifecycle.Persisted;
        }
    }

    /// <summary>
    /// 刷新最后活跃时刻 — 每次 Entity 有操作时调用
    /// </summary>
    public void Touch()
    {
        LastActivityAt = DateTime.UtcNow;
    }

    /// <summary>
    /// 是否已超时 — TimeoutAt 有值且当前时间已超过
    /// </summary>
    public bool IsTimedOut => TimeoutAt.HasValue && DateTime.UtcNow > TimeoutAt.Value;
}
