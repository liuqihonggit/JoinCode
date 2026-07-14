namespace Core.Utils;

public sealed class CategorizedRegistry<TKey, TValue, TCategory> where TKey : notnull
{
    private readonly CachedRegistry<TKey, TValue> _registry;
    private readonly Dictionary<TKey, TCategory> _categories;
    private readonly Func<TValue, bool>? _isEnabled;
    private readonly TCategory _defaultCategory;
    private IReadOnlyList<CategorizedEntry<TKey, TValue, TCategory>>? _cachedEntries;

    public int Count => _registry.Count;

    public CategorizedRegistry(
        TCategory defaultCategory,
        Func<TValue, bool>? isEnabled = null,
        IEqualityComparer<TKey>? comparer = null)
    {
        _registry = new CachedRegistry<TKey, TValue>(comparer);
        _categories = new Dictionary<TKey, TCategory>(comparer ?? EqualityComparer<TKey>.Default);
        _isEnabled = isEnabled;
        _defaultCategory = defaultCategory;
    }

    public void Register(TKey key, TValue value, bool isCanonical = true)
    {
        _registry.Register(key, value, isCanonical);
        _cachedEntries = null;
    }

    public void RegisterAlias(TKey alias, TValue value)
    {
        _registry.RegisterAlias(alias, value);
        _cachedEntries = null;
    }

    public void SetCategory(TKey key, TCategory category)
    {
        _categories[key] = category;
        _cachedEntries = null;
    }

    public bool Unregister(TKey key)
    {
        var removed = _registry.Unregister(key);
        if (removed) _cachedEntries = null;
        return removed;
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        if (!_registry.TryGetValue(key, out value))
            return false;

        if (_isEnabled is not null && !_isEnabled(value))
        {
            value = default;
            return false;
        }

        return true;
    }

    public bool ContainsKey(TKey key)
    {
        if (!_registry.TryGetValue(key, out var value))
            return false;

        return _isEnabled is null || _isEnabled(value);
    }

    public IReadOnlyDictionary<TKey, TValue> GetAllCanonical() => _registry.GetAllCanonical();

    public IReadOnlyList<CategorizedEntry<TKey, TValue, TCategory>> GetCategorizedEntries()
    {
        return _cachedEntries ??= _registry.GetCanonicalEntries()
            .Select(kvp => new CategorizedEntry<TKey, TValue, TCategory>(
                kvp.Key,
                kvp.Value,
                _categories.TryGetValue(kvp.Key, out var cat) ? cat : _defaultCategory,
                _isEnabled is null || _isEnabled(kvp.Value)))
            .ToArray();
    }
}

public sealed record CategorizedEntry<TKey, TValue, TCategory>(TKey Key, TValue Value, TCategory Category, bool IsEnabled);
