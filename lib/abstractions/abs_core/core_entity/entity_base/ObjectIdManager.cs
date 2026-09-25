namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 全局对象ID管理器 — 静态类，进程级全局唯一，无需DI
/// 每个类型注册到全局 map，方便遍历全局数据进行持久化
/// 基于 ImmutableDictionary + 无锁 CAS，消除 ConcurrentDictionary 锁竞争
/// </summary>
public static class ObjectIdManager {
    private static ImmutableDictionary<ObjectId, object> _objects = ImmutableDictionary<ObjectId, object>.Empty;
    private static ImmutableDictionary<Type, ImmutableList<ObjectId>> _typeIndex = ImmutableDictionary<Type, ImmutableList<ObjectId>>.Empty;

    /// <summary>
    /// 注册对象到全局管理器
    /// </summary>
    public static void Register<T>(T obj, ObjectId id) where T : notnull {
        ArgumentNullException.ThrowIfNull(obj);

        var added = false;
        ImmutableInterlocked.Update(ref _objects, d => {
            if (d.ContainsKey(id)) return d;
            added = true;
            return d.Add(id, obj);
        });
        if (!added) return;

        ImmutableInterlocked.Update(ref _typeIndex,
            d => d.TryGetValue(typeof(T), out var list)
                ? d.SetItem(typeof(T), list.Add(id))
                : d.Add(typeof(T), ImmutableList.Create(id)));
    }

    /// <summary>
    /// 注销对象
    /// </summary>
    public static bool Unregister(ObjectId id) {
        object? removed = null;
        ImmutableInterlocked.Update(ref _objects, d => {
            if (!d.TryGetValue(id, out var o)) return d;
            removed = o;
            return d.Remove(id);
        });
        if (removed is null) return false;

        var type = removed.GetType();
        ImmutableInterlocked.Update(ref _typeIndex,
            d => d.TryGetValue(type, out var list) ? d.SetItem(type, list.Remove(id)) : d);

        return true;
    }

    /// <summary>
    /// 获取对象 — 按类型转换
    /// </summary>
    public static T? Get<T>(ObjectId id) where T : class {
        if (Volatile.Read(ref _objects).TryGetValue(id, out var obj) && obj is T typed)
            return typed;
        return null;
    }

    /// <summary>
    /// 获取对象 — 不转换类型
    /// </summary>
    public static bool TryGet(ObjectId id, [NotNullWhen(true)] out object? obj) {
        return Volatile.Read(ref _objects).TryGetValue(id, out obj);
    }

    /// <summary>
    /// 获取指定类型的所有对象
    /// </summary>
    public static IReadOnlyList<T> GetAll<T>() where T : class {
        var ids = Volatile.Read(ref _typeIndex).GetValueOrDefault(typeof(T));
        if (ids is null || ids.Count == 0) return [];

        var objects = Volatile.Read(ref _objects);
        var result = new List<T>(ids.Count);
        foreach (var id in ids) {
            if (objects.TryGetValue(id, out var obj) && obj is T typed)
                result.Add(typed);
        }
        return result;
    }

    /// <summary>
    /// 当前注册的对象总数
    /// </summary>
    public static int Count => Volatile.Read(ref _objects).Count;

    /// <summary>
    /// 检查指定 ObjectId 是否已注册 — 用于后台扫描验证资源是否正确卸载
    /// </summary>
    public static bool IsRegistered(ObjectId id) => Volatile.Read(ref _objects).ContainsKey(id);

    /// <summary>
    /// 清空所有注册（测试用）
    /// </summary>
    public static void Clear() {
        Interlocked.Exchange(ref _objects, ImmutableDictionary<ObjectId, object>.Empty);
        Interlocked.Exchange(ref _typeIndex, ImmutableDictionary<Type, ImmutableList<ObjectId>>.Empty);
    }
}
