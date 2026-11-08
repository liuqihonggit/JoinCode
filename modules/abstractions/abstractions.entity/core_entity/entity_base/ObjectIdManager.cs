namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 全局对象ID管理器 — 静态类，进程级全局唯一，无需DI
/// 每个类型注册到全局 map，方便遍历全局数据进行持久化
/// 基于 ImmutableDictionary + 无锁 CAS，消除 ConcurrentDictionary 锁竞争
/// _typeIndex 用 LongRangeSet 区间压缩存储 SequenceId（ADR 0117）— 字符串只在 _objects 存一次
/// </summary>
public static class ObjectIdManager {
    private static volatile ImmutableHamT<ObjectId, object> _objects = ImmutableHamT<ObjectId, object>.Empty;
    private static volatile ImmutableHamT<Type, (ObjectType ObjType, LongRangeSet Ranges)> _typeIndex = ImmutableHamT<Type, (ObjectType, LongRangeSet)>.Empty;

    /// <summary>
    /// 注册对象到全局管理器 — 手写 CAS 循环替代 ImmutableInterlocked.Update(volatile 字段触发 CS0420)
    /// </summary>
    public static void Register<T>(T obj, ObjectId id) where T : notnull {
        ArgumentNullException.ThrowIfNull(obj);

        var added = false;
        while (true) {
            var current = _objects;
            if (current.ContainsKey(id)) break;
            var updated = current.Add(id, obj);
            if (Interlocked.CompareExchange(ref _objects, updated, current) == current) { added = true; break; }
        }
        if (!added) return;

        while (true) {
            var current = _typeIndex;
            var updated = current.TryGetValue(typeof(T), out var entry)
                ? current.SetItem(typeof(T), (entry.ObjType, entry.Ranges.Add(id.SequenceId)))
                : current.Add(typeof(T), (id.Type, LongRangeSet.Empty.Add(id.SequenceId)));
            if (Interlocked.CompareExchange(ref _typeIndex, updated, current) == current) break;
        }
    }

    /// <summary>
    /// 注销对象 — 手写 CAS 循环替代 ImmutableInterlocked.Update(volatile 字段触发 CS0420)
    /// </summary>
    public static bool Unregister(ObjectId id) {
        object? removed = null;
        while (true) {
            var current = _objects;
            if (!current.TryGetValue(id, out var o)) break;
            var updated = current.Remove(id);
            if (Interlocked.CompareExchange(ref _objects, updated, current) == current) { removed = o; break; }
        }
        if (removed is null) return false;

        var type = removed.GetType();
        while (true) {
            var current = _typeIndex;
            var updated = current.TryGetValue(type, out var entry)
                ? current.SetItem(type, (entry.ObjType, entry.Ranges.Remove(id.SequenceId)))
                : current;
            if (Interlocked.CompareExchange(ref _typeIndex, updated, current) == current) break;
        }

        return true;
    }

    /// <summary>
    /// 获取对象 — 按类型转换
    /// </summary>
    public static T? Get<T>(ObjectId id) where T : class {
        if (_objects.TryGetValue(id, out var obj) && obj is T typed)
            return typed;
        return null;
    }

    /// <summary>
    /// 获取对象 — 不转换类型
    /// </summary>
    public static bool TryGet(ObjectId id, [NotNullWhen(true)] out object? obj) {
        return _objects.TryGetValue(id, out obj);
    }

    /// <summary>
    /// 获取指定类型的所有对象 — 枚举 LongRangeSet 区间 SequenceId 构造 lookup 键反查 _objects
    /// </summary>
    public static IReadOnlyList<T> GetAll<T>() where T : class {
        var entry = _typeIndex.GetValueOrDefault(typeof(T));
        if (entry.Ranges.IsEmpty) return [];

        var objects = _objects;
        var result = new List<T>((int)Math.Min(entry.Ranges.Count, 1024));
        foreach (var seq in entry.Ranges.Enumerate()) {
            var lookupId = new ObjectId(entry.ObjType, seq);
            if (objects.TryGetValue(lookupId, out var obj) && obj is T typed)
                result.Add(typed);
        }
        return result;
    }

    /// <summary>
    /// 当前注册的对象总数
    /// </summary>
    public static int Count => _objects.Count;

    /// <summary>
    /// 检查指定 ObjectId 是否已注册 — 用于后台扫描验证资源是否正确卸载
    /// </summary>
    public static bool IsRegistered(ObjectId id) => _objects.ContainsKey(id);

    /// <summary>
    /// 清空所有注册（测试用）— 手写 CAS 循环替代 Interlocked.Exchange(volatile 字段触发 CS0420)
    /// </summary>
    public static void Clear() {
        while (true) {
            var current = _objects;
            if (Interlocked.CompareExchange(ref _objects, ImmutableHamT<ObjectId, object>.Empty, current) == current) break;
        }
        while (true) {
            var current = _typeIndex;
            if (Interlocked.CompareExchange(ref _typeIndex, ImmutableHamT<Type, (ObjectType, LongRangeSet)>.Empty, current) == current) break;
        }
    }
}
