namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 通用字典注册器基类 — 内部 ConcurrentDictionary，对外暴露 IEnumerable（遍历器）+ IReadOnlyDictionary（字典视图）
/// 脏标记缓存 FrozenDictionary，仅在增删时重建，避免每次调用分配新集合
/// 可选 Canonical/Alias 跟踪 — 子类需要区分正式名和别名时启用
/// </summary>
public class MapRegistry<TKey, TValue> where TKey : notnull
{
    private readonly ConcurrentDictionary<TKey, TValue> _items;
    private readonly HashSet<TKey> _canonicalKeys = new();
    private readonly bool _trackCanonical;
    private IReadOnlyDictionary<TKey, TValue> _cachedDict = FrozenDictionary<TKey, TValue>.Empty;
    private IReadOnlyDictionary<TKey, TValue> _cachedCanonical = FrozenDictionary<TKey, TValue>.Empty;
    private bool _cachedDictValid;
    private bool _cachedCanonicalValid;

    /// <summary>当前注册项总数</summary>
    public int Count => _items.Count;

    public MapRegistry(IEqualityComparer<TKey>? comparer = null, bool trackCanonical = false)
    {
        var c = comparer ?? EqualityComparer<TKey>.Default;
        _items = new ConcurrentDictionary<TKey, TValue>(c);
        _trackCanonical = trackCanonical;
        if (trackCanonical)
            _canonicalKeys = new HashSet<TKey>(c);
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

    /// <summary>注销项并返回被移除的值</summary>
    protected bool RemoveCore(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var removed = _items.TryRemove(key, out value);
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
    /// 键值对遍历器 — 返回 IEnumerable，不分配新集合
    /// </summary>
    protected IEnumerable<KeyValuePair<TKey, TValue>> EntriesCore => _items;

    /// <summary>
    /// 字典视图 — 返回 IReadOnlyDictionary，脏标记缓存 FrozenDictionary
    /// 调用方可按键查找 + 遍历，不分配新集合
    /// </summary>
    public IReadOnlyDictionary<TKey, TValue> AsDictionary()
    {
        if (!_cachedDictValid)
        {
            _cachedDict = _items.ToFrozenDictionary();
            _cachedDictValid = true;
        }
        return _cachedDict;
    }

    /// <summary>条件过滤遍历器 — 不分配新集合</summary>
    public IEnumerable<TValue> Where(Func<TValue, bool> predicate) => _items.Values.Where(predicate);

    /// <summary>清空所有注册（测试用）</summary>
    public void Clear()
    {
        _items.Clear();
        if (_trackCanonical)
            _canonicalKeys.Clear();
        InvalidateCache();
    }

    /// <summary>清空所有注册并返回被清空的项（子类需要在清空前执行清理逻辑时使用）</summary>
    protected List<KeyValuePair<TKey, TValue>> ClearCore()
    {
        var snapshot = new List<KeyValuePair<TKey, TValue>>(_items);
        _items.Clear();
        if (_trackCanonical)
            _canonicalKeys.Clear();
        InvalidateCache();
        return snapshot;
    }

    // === Canonical/Alias 支持（trackCanonical=true 时启用）===

    /// <summary>注册项（含 Canonical 标记）</summary>
    public void Register(TKey key, TValue value, bool isCanonical = true)
    {
        _items[key] = value;
        if (isCanonical && _trackCanonical)
            _canonicalKeys.Add(key);
        InvalidateCache();
    }

    /// <summary>注册别名（不覆盖已存在的项，不标记为 Canonical）</summary>
    public void RegisterAlias(TKey alias, TValue value)
    {
        _items.TryAdd(alias, value);
    }

    /// <summary>注销项（公开方法，同时移除 Canonical 标记）</summary>
    public bool Unregister(TKey key)
    {
        var removed = _items.TryRemove(key, out _);
        if (_trackCanonical)
            _canonicalKeys.Remove(key);
        if (removed) InvalidateCache();
        return removed;
    }

    /// <summary>获取所有 Canonical 项的字典视图 — 脏标记缓存 FrozenDictionary</summary>
    public IReadOnlyDictionary<TKey, TValue> GetAllCanonical()
    {
        if (!_trackCanonical)
            return AsDictionary();
        if (!_cachedCanonicalValid)
        {
            _cachedCanonical = _canonicalKeys
                .ToDictionary(n => n, n => _items[n])
                .ToFrozenDictionary();
            _cachedCanonicalValid = true;
        }
        return _cachedCanonical;
    }

    /// <summary>获取所有 Canonical 项的键值对遍历器</summary>
    public IEnumerable<KeyValuePair<TKey, TValue>> GetCanonicalEntries()
    {
        if (!_trackCanonical)
            return _items;
        return _canonicalKeys.Select(n => new KeyValuePair<TKey, TValue>(n, _items[n]));
    }

    private void InvalidateCache()
    {
        _cachedDictValid = false;
        _cachedCanonicalValid = false;
    }
}
