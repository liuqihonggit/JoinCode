namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话隔离多级路由 — 进程级单例，静态类无需 DI
/// 第一级: map&lt;ObjectId sessionId, SessionScope scope&gt; 会话隔离
/// 第二级: SessionScope 内 map&lt;ObjectType, HashSet&lt;ObjectId&gt;&gt; 类型分桶
/// 第三级: SessionScope 内 map&lt;ObjectId, Entity&gt; 实际存储
/// 插件通过 Resolve&lt;T&gt;(sessionId, entityId) 跳转获取，避免跨会话误用 ObjectId
/// </summary>
public static class SessionRouter
{
    private static readonly ConcurrentDictionary<ObjectId, SessionScope> _scopes = new();

    /// <summary>当前会话作用域总数</summary>
    public static int ScopeCount => _scopes.Count;

    /// <summary>
    /// 创建或获取会话作用域 — 幂等，相同 sessionId 返回同一实例
    /// </summary>
    public static SessionScope GetOrCreateScope(ObjectId sessionId)
    {
        if (sessionId.IsEmpty)
            throw new ArgumentException("SessionId 不能为空", nameof(sessionId));
        return _scopes.GetOrAdd(sessionId, id => new SessionScope(id));
    }

    /// <summary>获取会话作用域 — 不存在返回 null</summary>
    public static SessionScope? GetScope(ObjectId sessionId)
        => _scopes.GetValueOrDefault(sessionId);

    /// <summary>尝试获取会话作用域</summary>
    public static bool TryGetScope(ObjectId sessionId, [NotNullWhen(true)] out SessionScope? scope)
        => _scopes.TryGetValue(sessionId, out scope);

    /// <summary>
    /// 跳转获取 — 插件通过 (sessionId, entityId) 获取强类型 Entity
    /// 会话不存在或 Entity 不属于该会话均返回 null，保证跨会话隔离
    /// </summary>
    public static T? Resolve<T>(ObjectId sessionId, ObjectId entityId) where T : Entity
    {
        if (!_scopes.TryGetValue(sessionId, out var scope))
            return null;
        return scope.Resolve<T>(entityId);
    }

    /// <summary>跳转获取 — 不转换类型</summary>
    public static bool TryResolve(ObjectId sessionId, ObjectId entityId, [NotNullWhen(true)] out Entity? entity)
    {
        entity = null;
        return _scopes.TryGetValue(sessionId, out var scope) && scope.TryGet(entityId, out entity);
    }

    /// <summary>获取所有会话作用域 — 不分配新集合</summary>
    public static IEnumerable<SessionScope> GetAllScopes() => _scopes.Values;

    /// <summary>
    /// 移除会话作用域 — Dispose 其所有 Entity，返回是否移除成功
    /// </summary>
    public static bool RemoveScope(ObjectId sessionId)
    {
        if (!_scopes.TryRemove(sessionId, out var scope))
            return false;
        scope.Dispose();
        return true;
    }

    /// <summary>清空所有会话作用域（测试用）— Dispose 每个作用域的所有 Entity</summary>
    public static void Clear()
    {
        foreach (var scope in _scopes.Values)
        {
            try { scope.Dispose(); }
            catch (Exception ex) { _ = ex; }
        }
        _scopes.Clear();
    }

    /// <summary>
    /// 跨会话拷贝 — 将 Entity 深拷贝到目标会话，返回新副本
    /// 原 Entity 不变，两个独立副本互不影响
    /// 引用重映射通过 CloneContext 处理，找不到抛异常
    /// </summary>
    public static T CloneTo<T>(T entity, CloneContext context) where T : Entity
    {
        ArgumentNullException.ThrowIfNull(entity);
        return (T)entity.Clone(context);
    }
}
