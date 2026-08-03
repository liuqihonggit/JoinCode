namespace Core.Utils;

public sealed class CachedRegistry<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _items;
    private readonly HashSet<TKey> _canonicalKeys;
    private IReadOnlyDictionary<TKey, TValue>? _cachedAll;

    public int Count => _items.Count;

    public CachedRegistry(IEqualityComparer<TKey>? comparer = null)
    {
        var c = comparer ?? EqualityComparer<TKey>.Default;
        _items = new Dictionary<TKey, TValue>(c);
        _canonicalKeys = new HashSet<TKey>(c);
    }

    public void Register(TKey key, TValue value, bool isCanonical = true)
    {
        _items[key] = value;
        if (isCanonical)
            _canonicalKeys.Add(key);
        InvalidateCache();
    }

    public void RegisterAlias(TKey alias, TValue value)
    {
        if (!_items.ContainsKey(alias))
            _items[alias] = value;
    }

    public bool Unregister(TKey key)
    {
        var removed = _items.Remove(key);
        _canonicalKeys.Remove(key);
        if (removed) InvalidateCache();
        return removed;
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _items.TryGetValue(key, out value);

    public bool ContainsKey(TKey key) => _items.ContainsKey(key);

    /// <summary>
    /// 字典视图 — 返回 IReadOnlyDictionary，脏标记缓存 FrozenDictionary
    /// </summary>
    public IReadOnlyDictionary<TKey, TValue> GetAllCanonical()
    {
        return _cachedAll ??= _canonicalKeys
            .ToDictionary(n => n, n => _items[n])
            .ToFrozenDictionary();
    }

    /// <summary>
    /// 遍历器 — 返回 IEnumerable，不分配新集合
    /// </summary>
    public IEnumerable<KeyValuePair<TKey, TValue>> GetCanonicalEntries()
    {
        return _canonicalKeys.Select(n => new KeyValuePair<TKey, TValue>(n, _items[n]));
    }

    private void InvalidateCache()
    {
        _cachedAll = null;
    }
}
