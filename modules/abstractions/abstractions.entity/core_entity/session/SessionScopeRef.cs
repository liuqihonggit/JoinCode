namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话作用域借用句柄 — 方案 D：消除 JCC9305 误报
/// 调用方从 SessionRouter.GetScope/GetOrCreateScope 获取 scope 后只读取成员不释放（借用），
/// 但分析器按"IDisposable 变量即拥有"规则报泄露。此句柄为 readonly struct，不实现 IDisposable/IAsyncDisposable，
/// 转发 SessionScope 所有公共只读成员，借用语义对分析器透明。
/// 真实 SessionScope 的 DisposeAsync 仍由 SessionRouter.RemoveScopeAsync/ClearAsync 内部调用。
/// </summary>
public readonly struct SessionScopeRef {
    private readonly SessionScope _scope;

    /// <summary>构造借用句柄 — 仅同程序集（SessionRouter）可调用</summary>
    internal SessionScopeRef(SessionScope scope) => _scope = scope;

    /// <summary>此作用域的会话 ObjectId</summary>
    public ObjectId SessionId => _scope.SessionId;

    /// <summary>会话级缓存 — 缓存项派生 Entity, 纳入回收体系</summary>
    public ISessionCache Cache => _scope.Cache;

    /// <summary>当前注册的 Entity 总数</summary>
    public int Count => _scope.Count;

    /// <summary>是否已释放</summary>
    public bool IsDisposed => _scope.IsDisposed;

    /// <summary>Dispose 期间失败的 Entity 数量 — 诊断用，0 表示全部成功</summary>
    public int DisposeFailures => _scope.DisposeFailures;

    /// <summary>注册 Entity 到此会话作用域 — 已存在则不覆盖</summary>
    public void Register(Entity entity) => _scope.Register(entity);

    /// <summary>注销 Entity — 返回是否移除成功</summary>
    public bool Unregister(ObjectId entityId) => _scope.Unregister(entityId);

    /// <summary>跳转获取 — 插件通过 (sessionId, entityId) 获取强类型 Entity</summary>
    public T? Resolve<T>(ObjectId entityId) where T : Entity
        => _scope.Resolve<T>(entityId);

    /// <summary>尝试获取 — 不转换类型</summary>
    public bool TryGet(ObjectId entityId, [NotNullWhen(true)] out Entity? entity)
        => _scope.TryGet(entityId, out entity);

    /// <summary>是否包含指定 Entity</summary>
    public bool Contains(ObjectId entityId) => _scope.Contains(entityId);

    /// <summary>获取此会话所有 Entity 的快照拷贝</summary>
    public Entity[] GetAll() => _scope.GetAll();

    /// <summary>按 ObjectType 分桶获取 — O(1) 索引查找</summary>
    public IEnumerable<Entity> GetAll(ObjectType type) => _scope.GetAll(type);

    /// <summary>按 CLR 类型获取所有 — 遍历过滤，调用方友好</summary>
    public IReadOnlyList<T> GetAll<T>() where T : Entity => _scope.GetAll<T>();
}
