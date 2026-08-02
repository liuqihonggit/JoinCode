namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 实体基类 — 所有有生命周期的实体派生此类
/// 共同属性：ObjectId + CreatedAt + StartedAt + CompletedAt + 惰性释放
/// 加一个共同属性只改此处，不需要改所有子类
/// </summary>
public abstract class Entity : IDisposable
{
    public ObjectId ObjectId { get; }
    public string Id => ObjectId.Id;
    public DateTime CreatedAt { get; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    private bool _disposed;

    protected Entity(ObjectType type, string? id = null)
    {
        ObjectId = new ObjectId(type, id ?? GenerateId(type));
        CreatedAt = DateTime.UtcNow;
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
        OnDispose();
        ObjectIdManager.Unregister(ObjectId);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// 子类覆写：从各自 Registry 移除、释放资源等
    /// </summary>
    protected abstract void OnDispose();

    private static string GenerateId(ObjectType type)
    {
        var prefix = type.ToValue();
        var guid = Guid.NewGuid().ToString("N")[..8];
        return $"{prefix}-{guid}";
    }
}
