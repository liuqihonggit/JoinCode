namespace Core.Utils;

public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly int _maxEntries;
    private readonly long _maxSizeBytes;
    private readonly Func<TValue, long> _sizeCalculator;
    private readonly LinkedList<TKey> _lruOrder = new();
    private readonly Dictionary<TKey, LinkedListNode<TKey>> _keyNodes;
    private readonly Dictionary<TKey, TValue> _cache;
    private long _currentSizeBytes;

    public int Count => _cache.Count;
    public long CurrentSizeBytes => _currentSizeBytes;

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

    public bool ContainsKey(TKey key) => _cache.ContainsKey(key);

    public bool Remove(TKey key)
    {
        if (!_cache.TryGetValue(key, out var existing))
            return false;

        _currentSizeBytes -= Math.Max(1, _sizeCalculator(existing));
        _cache.Remove(key);
        RemoveFromLru(key);
        return true;
    }

    public void Clear()
    {
        _cache.Clear();
        _keyNodes.Clear();
        _lruOrder.Clear();
        _currentSizeBytes = 0;
    }

    public IEnumerable<TKey> Keys()
    {
        foreach (var key in _lruOrder)
            yield return key;
    }

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
