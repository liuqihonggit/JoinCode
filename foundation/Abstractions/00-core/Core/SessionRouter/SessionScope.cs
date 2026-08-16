namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话作用域 — 每个会话是一个独立世界，所有 Entity 不跨会话
/// 内部结构: map&lt;ObjectId, Entity&gt; 实际存储 + map&lt;ObjectType, HashSet&lt;ObjectId&gt;&gt; 类型分桶索引
/// 会话 Dispose 时清理其所有 Entity
/// </summary>
public sealed class SessionScope : IDisposable
{
    private readonly ConcurrentDictionary<ObjectId, Entity> _entities = new();
    private readonly ConcurrentDictionary<ObjectType, HashSet<ObjectId>> _typeIndex = new();
    private readonly object _indexLock = new();
    private volatile bool _disposed;
    private int _disposeFailures;

    /// <summary>此作用域的会话 ObjectId</summary>
    public ObjectId SessionId { get; }

    /// <summary>会话级缓存 — 缓存项派生 Entity, 纳入回收体系</summary>
    public ISessionCache Cache { get; }

    /// <summary>当前注册的 Entity 总数</summary>
    public int Count => _entities.Count;

    /// <summary>是否已释放</summary>
    public bool IsDisposed => _disposed;

    /// <summary>Dispose 期间失败的 Entity 数量 — 诊断用，0 表示全部成功</summary>
    public int DisposeFailures => _disposeFailures;

    internal SessionScope(ObjectId sessionId)
    {
        if (sessionId.IsEmpty)
            throw new ArgumentException("SessionId 不能为空", nameof(sessionId));
        SessionId = sessionId;
        Cache = new SessionCache(sessionId);
    }

    /// <summary>
    /// 注册 Entity 到此会话作用域 — 已存在则不覆盖
    /// </summary>
    public void Register(Entity entity)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(entity);
        if (!_entities.TryAdd(entity.ObjectId, entity)) return;
        AddToTypeIndex(entity);
    }

    /// <summary>
    /// 注销 Entity — 返回是否移除成功
    /// </summary>
    public bool Unregister(ObjectId entityId)
    {
        if (!_entities.TryRemove(entityId, out var entity)) return false;
        RemoveFromTypeIndex(entity);
        return true;
    }

    /// <summary>
    /// 跳转获取 — 插件通过 (sessionId, entityId) 获取强类型 Entity
    /// AOT 友好，无反射，类型不匹配返回 null
    /// </summary>
    public T? Resolve<T>(ObjectId entityId) where T : Entity
        => _entities.TryGetValue(entityId, out var e) && e is T typed ? typed : null;

    /// <summary>
    /// 尝试获取 — 不转换类型
    /// </summary>
    public bool TryGet(ObjectId entityId, [NotNullWhen(true)] out Entity? entity)
        => _entities.TryGetValue(entityId, out entity);

    /// <summary>是否包含指定 Entity</summary>
    public bool Contains(ObjectId entityId) => _entities.ContainsKey(entityId);

    /// <summary>获取此会话所有 Entity — 不分配新集合</summary>
    public IEnumerable<Entity> GetAll() => _entities.Values;

    /// <summary>
    /// 按 ObjectType 分桶获取 — O(1) 索引查找，对应注册工厂 map(ObjectType -&gt; HashSet of ObjectId)
    /// </summary>
    public IEnumerable<Entity> GetAll(ObjectType type)
    {
        if (!_typeIndex.TryGetValue(type, out var ids)) yield break;
        foreach (var id in ids)
        {
            if (_entities.TryGetValue(id, out var e))
                yield return e;
        }
    }

    /// <summary>
    /// 按 CLR 类型获取所有 — 遍历过滤，调用方友好
    /// </summary>
    public IReadOnlyList<T> GetAll<T>() where T : Entity
    {
        var result = new List<T>();
        foreach (var entity in _entities.Values)
        {
            if (entity is T typed)
                result.Add(typed);
        }
        return result;
    }

    /// <summary>
    /// 释放此会话作用域 — Dispose 所有注册的 Entity，清空索引
    /// 单个 Entity Dispose 失败不中断其他 Entity 清理，失败计数记录到 DisposeFailures
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Cache.Clear();

        foreach (var entity in _entities.Values)
        {
            try { entity.Dispose(); }
            catch (Exception) { Interlocked.Increment(ref _disposeFailures); }
        }

        _entities.Clear();
        _typeIndex.Clear();
    }

    private void AddToTypeIndex(Entity entity)
    {
        var type = entity.ObjectId.Type;
        var set = _typeIndex.GetOrAdd(type, _ => new HashSet<ObjectId>());
        lock (_indexLock)
        {
            set.Add(entity.ObjectId);
        }
    }

    private void RemoveFromTypeIndex(Entity entity)
    {
        var type = entity.ObjectId.Type;
        if (_typeIndex.TryGetValue(type, out var set))
        {
            lock (_indexLock)
            {
                set.Remove(entity.ObjectId);
            }
        }
    }
}
