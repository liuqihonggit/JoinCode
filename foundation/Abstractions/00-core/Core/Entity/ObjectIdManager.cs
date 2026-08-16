namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 全局对象ID管理器 — 静态类，进程级全局唯一，无需DI
/// 每个类型注册到全局 map，方便遍历全局数据进行持久化
/// </summary>
public static class ObjectIdManager
{
    private static readonly ConcurrentDictionary<ObjectId, object> _objects = new();
    private static readonly ConcurrentDictionary<Type, List<ObjectId>> _typeIndex = new();

    /// <summary>
    /// 注册对象到全局管理器
    /// </summary>
    public static void Register<T>(T obj, ObjectId id) where T : notnull
    {
        ArgumentNullException.ThrowIfNull(obj);

        if (!_objects.TryAdd(id, obj))
            return;

        _typeIndex.AddOrUpdate(
            typeof(T),
            _ => [id],
            (_, list) => { lock (list) { list.Add(id); } return list; });
    }

    /// <summary>
    /// 注销对象
    /// </summary>
    public static bool Unregister(ObjectId id)
    {
        if (!_objects.TryRemove(id, out var obj))
            return false;

        var type = obj.GetType();
        if (_typeIndex.TryGetValue(type, out var list))
        {
            lock (list)
            {
                list.Remove(id);
            }
        }

        return true;
    }

    /// <summary>
    /// 获取对象 — 按类型转换
    /// </summary>
    public static T? Get<T>(ObjectId id) where T : class
    {
        if (_objects.TryGetValue(id, out var obj) && obj is T typed)
            return typed;
        return null;
    }

    /// <summary>
    /// 获取对象 — 不转换类型
    /// </summary>
    public static bool TryGet(ObjectId id, [NotNullWhen(true)] out object? obj)
    {
        return _objects.TryGetValue(id, out obj);
    }

    /// <summary>
    /// 获取指定类型的所有对象
    /// </summary>
    public static IReadOnlyList<T> GetAll<T>() where T : class
    {
        if (!_typeIndex.TryGetValue(typeof(T), out var ids))
            return [];

        lock (ids)
        {
            var result = new List<T>(ids.Count);
            foreach (var id in ids)
            {
                if (_objects.TryGetValue(id, out var obj) && obj is T typed)
                    result.Add(typed);
            }
            return result;
        }
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
    /// 清空所有注册（测试用）
    /// </summary>
    public static void Clear()
    {
        _objects.Clear();
        _typeIndex.Clear();
    }
}
