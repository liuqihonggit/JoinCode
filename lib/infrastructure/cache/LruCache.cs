namespace Core.Utils;

/// <summary>
/// LRU 缓存 — 基于双向链表 + 字典实现,支持按条目数和按字节容量双重淘汰策略
/// </summary>
public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _maxEntries;
    private readonly long _maxSizeBytes;
    private readonly Func<TValue, long> _sizeCalculator;
    private readonly LinkedList<TKey> _lruOrder = new();
    private readonly Dictionary<TKey, LinkedListNode<TKey>> _keyNodes;
    private readonly Dictionary<TKey, TValue> _cache;
    private long _currentSizeBytes;

    /// <summary>当前缓存条目数</summary>
    public int Count => _cache.Count;

    /// <summary>当前缓存总字节大小</summary>
    public long CurrentSizeBytes => _currentSizeBytes;

    /// <summary>
    /// 构造 LRU 缓存
    /// </summary>
    /// <param name="maxEntries">最大条目数,默认 100</param>
    /// <param name="maxSizeBytes">最大字节容量,默认无限制</param>
    /// <param name="sizeCalculator">条目字节大小计算函数,默认每条目计 1 字节</param>
    /// <param name="comparer">键相等比较器,默认使用 EqualityComparer&lt;TKey&gt;.Default</param>
    public LruCache(
        int maxEntries = 100,
        long maxSizeBytes = long.MaxValue,
        Func<TValue, long>? sizeCalculator = null,
        IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);
        _maxEntries = maxEntries;
        _maxSizeBytes = maxSizeBytes;
        _sizeCalculator = sizeCalculator ?? (_ => 1);
        var c = comparer ?? EqualityComparer<TKey>.Default;
        _keyNodes = new Dictionary<TKey, LinkedListNode<TKey>>(c);
        _cache = new Dictionary<TKey, TValue>(c);
    }

    /// <summary>
    /// 设置缓存条目,若键已存在则更新,必要时淘汰最久未使用条目
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="value">缓存值</param>
    public void Set(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);

        var entrySize = Math.Max(1, _sizeCalculator(value));

        if (_cache.TryGetValue(key, out var existing))
        {
            _currentSizeBytes -= Math.Max(1, _sizeCalculator(existing));
            RemoveFromLru(key);
        }

        while (_currentSizeBytes + entrySize > _maxSizeBytes && _lruOrder.Count > 0)
            EvictOldest();

        while (_cache.Count >= _maxEntries && _lruOrder.Count > 0)
            EvictOldest();

        _cache[key] = value;
        _currentSizeBytes += entrySize;
        PromoteInLru(key);
    }

    /// <summary>
    /// 尝试获取缓存值,命中时将该键提升为最近使用
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <param name="value">命中时输出值,未命中时为 default</param>
    /// <returns>是否命中</returns>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (_cache.TryGetValue(key, out value))
        {
            PromoteInLru(key);
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 判断是否包含指定键
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <returns>是否包含</returns>
    public bool ContainsKey(TKey key) => _cache.ContainsKey(key);

    /// <summary>
    /// 移除指定键的缓存条目
    /// </summary>
    /// <param name="key">缓存键</param>
    /// <returns>是否实际移除</returns>
    public bool Remove(TKey key)
    {
        if (!_cache.TryGetValue(key, out var existing))
            return false;

        _currentSizeBytes -= Math.Max(1, _sizeCalculator(existing));
        _cache.Remove(key);
        RemoveFromLru(key);
        return true;
    }

    /// <summary>
    /// 清空所有缓存条目
    /// </summary>
    public void Clear()
    {
        _cache.Clear();
        _keyNodes.Clear();
        _lruOrder.Clear();
        _currentSizeBytes = 0;
    }

    /// <summary>
    /// 按 LRU 顺序(最近到最久)枚举所有键
    /// </summary>
    /// <returns>键的枚举器</returns>
    public IEnumerable<TKey> Keys()
    {
        foreach (var key in _lruOrder)
            yield return key;
    }

    /// <summary>
    /// 按 LRU 顺序(最近到最久)枚举所有键值对
    /// </summary>
    /// <returns>键值对的枚举器</returns>
    public IEnumerable<KeyValuePair<TKey, TValue>> Entries()
    {
        foreach (var key in _lruOrder)
        {
            if (_cache.TryGetValue(key, out var value))
                yield return new KeyValuePair<TKey, TValue>(key, value);
        }
    }

    private void EvictOldest()
    {
        if (_lruOrder.Last is null) return;

        var oldestKey = _lruOrder.Last.Value;
        _lruOrder.RemoveLast();

        if (_keyNodes.TryGetValue(oldestKey, out var node) && node.List == _lruOrder)
            _lruOrder.Remove(node);
        _keyNodes.Remove(oldestKey);

        if (_cache.TryGetValue(oldestKey, out var existing))
        {
            _currentSizeBytes -= Math.Max(1, _sizeCalculator(existing));
            _cache.Remove(oldestKey);
        }
    }

    private void PromoteInLru(TKey key)
    {
        RemoveFromLru(key);
        var node = _lruOrder.AddFirst(key);
        _keyNodes[key] = node;
    }

    private void RemoveFromLru(TKey key)
    {
        if (_keyNodes.TryGetValue(key, out var node))
        {
            if (node.List is not null)
                node.List.Remove(node);
            _keyNodes.Remove(key);
        }
    }
}
