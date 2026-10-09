// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 会话隔离多级路由 — 进程级单例，静态类无需 DI
/// 第一级: map&lt;ObjectId sessionId, SessionScope scope&gt; 会话隔离
/// 第二级: SessionScope 内 map&lt;ObjectType, HashSet&lt;ObjectId&gt;&gt; 类型分桶
/// 第三级: SessionScope 内 map&lt;ObjectId, Entity&gt; 实际存储
/// 插件通过 Resolve&lt;T&gt;(sessionId, entityId) 跳转获取，避免跨会话误用 ObjectId
/// </summary>
public static class SessionRouter {
    private static ImmutableHamT<ObjectId, SessionScope> _scopes = ImmutableHamT<ObjectId, SessionScope>.Empty;

    /// <summary>当前会话作用域总数</summary>
    public static int ScopeCount => Volatile.Read(ref _scopes).Count;

    /// <summary>
    /// 创建或获取会话作用域 — 幂等，相同 sessionId 返回同一实例
    /// 返回借用句柄 SessionScopeRef（不实现 IDisposable），消除 JCC9305 误报；
    /// 真实 SessionScope 的 DisposeAsync 由 RemoveScopeAsync/ClearAsync 内部调用
    /// </summary>
    public static SessionScopeRef GetOrCreateScope(ObjectId sessionId) {
        if (sessionId.IsEmpty)
            throw new ArgumentException("SessionId 不能为空", nameof(sessionId));

        var current = Volatile.Read(ref _scopes);
        if (current.TryGetValue(sessionId, out var existing))
            return new SessionScopeRef(existing);

        var newScope = new SessionScope(sessionId);
        ImmutableInterlocked.Update(ref _scopes, d => d.ContainsKey(sessionId) ? d : d.Add(sessionId, newScope));
        return new SessionScopeRef(Volatile.Read(ref _scopes)[sessionId]);
    }

    /// <summary>获取会话作用域借用句柄 — 不存在返回 null</summary>
    public static SessionScopeRef? GetScope(ObjectId sessionId)
        => Volatile.Read(ref _scopes).TryGetValue(sessionId, out var scope) ? new SessionScopeRef(scope) : null;

    /// <summary>尝试获取会话作用域借用句柄</summary>
    public static bool TryGetScope(ObjectId sessionId, [NotNullWhen(true)] out SessionScopeRef? scope) {
        if (Volatile.Read(ref _scopes).TryGetValue(sessionId, out var s)) {
            scope = new SessionScopeRef(s);
            return true;
        }
        scope = null;
        return false;
    }

    /// <summary>
    /// 跳转获取 — 插件通过 (sessionId, entityId) 获取强类型 Entity
    /// 会话不存在或 Entity 不属于该会话均返回 null，保证跨会话隔离
    /// </summary>
    public static T? Resolve<T>(ObjectId sessionId, ObjectId entityId) where T : Entity {
        if (!Volatile.Read(ref _scopes).TryGetValue(sessionId, out var scope))
            return null;
        return scope.Resolve<T>(entityId);
    }

    /// <summary>跳转获取 — 不转换类型</summary>
    public static bool TryResolve(ObjectId sessionId, ObjectId entityId, [NotNullWhen(true)] out Entity? entity) {
        entity = null;
        return Volatile.Read(ref _scopes).TryGetValue(sessionId, out var scope) && scope.TryGet(entityId, out entity);
    }

    /// <summary>获取所有会话作用域借用句柄的快照拷贝</summary>
    public static SessionScopeRef[] GetAllScopes()
        => Volatile.Read(ref _scopes).Values.Select(s => new SessionScopeRef(s)).ToArray();

    /// <summary>
    /// 移除会话作用域 — DisposeAsync 其所有 Entity，返回是否移除成功
    /// </summary>
    public static async Task<bool> RemoveScopeAsync(ObjectId sessionId) {
        var hadScope = Volatile.Read(ref _scopes).TryGetValue(sessionId, out var scope);
        ImmutableInterlocked.Update(ref _scopes, d => d.Remove(sessionId));
        if (!hadScope) return false;
        await scope!.DisposeAsync().ConfigureAwait(false);
        return true;
    }

    /// <summary>清空所有会话作用域（测试用）— DisposeAsync 每个作用域的所有 Entity</summary>
    public static async Task ClearAsync() {
        foreach (var scope in Volatile.Read(ref _scopes).Values) {
            try { await scope.DisposeAsync().ConfigureAwait(false); } catch (Exception ex) { _ = ex; }
        }
        Volatile.Write(ref _scopes, ImmutableHamT<ObjectId, SessionScope>.Empty);
    }

    /// <summary>
    /// 跨会话拷贝 — 将 Entity 深拷贝到目标会话，返回新副本
    /// 原 Entity 不变，两个独立副本互不影响
    /// 引用重映射通过 CloneContext 处理，找不到抛异常
    /// </summary>
    public static T CloneTo<T>(T entity, CloneContext context) where T : Entity {
        ArgumentNullException.ThrowIfNull(entity);
        return (T)entity.Clone(context);
    }
}