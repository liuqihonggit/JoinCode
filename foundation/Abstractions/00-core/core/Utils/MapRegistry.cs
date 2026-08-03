namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 通用字典注册器基类 — 内部 ConcurrentDictionary，对外暴露 IEnumerable（遍历器）+ IReadOnlyDictionary（字典视图）
/// 脏标记缓存 FrozenDictionary，仅在增删时重建，避免每次调用分配新集合
/// </summary>
public class MapRegistry<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _items;
    private IReadOnlyDictionary<TKey, TValue>? _cachedDict;

    /// <summary>当前注册项总数</summary>
    public int Count => _items.Count;

    public MapRegistry(IEqualityComparer<TKey>? comparer = null)
    {
        _items = new ConcurrentDictionary<TKey, TValue>(comparer ?? EqualityComparer<TKey>.Default);
    }

    /// <summary>注册项（已存在则不覆盖）</summary>
    protected void AddCore(TKey key, TValue value)
    {
        _items.TryAdd(key, value);
        InvalidateCache();
    }

    /// <summary>注册或更新项</summary>
    protected void AddOrUpdateCore(TKey key, TValue value)
    {
        _items[key] = value;
        InvalidateCache();
    }

    /// <summary>注销项</summary>
    protected bool RemoveCore(TKey key)
    {
        var removed = _items.TryRemove(key, out _);
        if (removed) InvalidateCache();
        return removed;
    }

    /// <summary>按键获取（O(1)）</summary>
    public TValue? Get(TKey key) => _items.GetValueOrDefault(key);

    /// <summary>按键尝试获取（O(1)）</summary>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _items.TryGetValue(key, out value);

    /// <summary>是否包含指定键</summary>
    public bool ContainsKey(TKey key) => _items.ContainsKey(key);

    /// <summary>
    /// 遍历器 — 返回 IEnumerable，不分配新集合，调用方只能遍历
    /// </summary>
    public IEnumerable<TValue> GetAll() => _items.Values;

    /// <summary>
    /// 字典视图 — 返回 IReadOnlyDictionary，脏标记缓存 FrozenDictionary
    /// 调用方可按键查找 + 遍历，不分配新集合
    /// </summary>
    public IReadOnlyDictionary<TKey, TValue> AsDictionary()
    {
        return _cachedDict ??= _items.ToFrozenDictionary();
    }

    /// <summary>条件过滤遍历器 — 不分配新集合</summary>
    public IEnumerable<TValue> Where(Func<TValue, bool> predicate) => _items.Values.Where(predicate);

    /// <summary>清空所有注册（测试用）</summary>
    public void Clear()
    {
        _items.Clear();
        InvalidateCache();
    }

    private void InvalidateCache() => _cachedDict = null;
}
