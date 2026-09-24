namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 次级索引 — 按 TValue 的属性建立 TProperty → keySet 索引，O(1) 查找
/// 注册/注销时由 MapRegistry 自动同步，子类通过 CreateIndex 声明
/// </summary>
public sealed class SecondaryIndex<TKey, TValue, TProperty>
    where TKey : notnull
    where TProperty : notnull {
    private ImmutableDictionary<TProperty, ImmutableHashSet<TKey>> _index = ImmutableDictionary<TProperty, ImmutableHashSet<TKey>>.Empty;
    private readonly Func<TValue, TProperty> _selector;

    internal SecondaryIndex(
        Func<TValue, TProperty> selector,
        IEqualityComparer<TProperty>? comparer = null) {
        _selector = selector;
        if (comparer != null)
            _index = ImmutableDictionary<TProperty, ImmutableHashSet<TKey>>.Empty.WithComparers(comparer);
    }

    /// <summary>注册时同步添加到索引</summary>
    internal void Add(TKey key, TValue value) {
        var prop = _selector(value);
        ImmutableInterlocked.Update(ref _index, d => {
            var set = d.GetValueOrDefault(prop) ?? ImmutableHashSet<TKey>.Empty;
            return d.SetItem(prop, set.Add(key));
        });
    }

    /// <summary>注销时同步从索引移除</summary>
    internal void Remove(TKey key, TValue value) {
        var prop = _selector(value);
        ImmutableInterlocked.Update(ref _index, d => {
            if (d.TryGetValue(prop, out var set)) {
                var newSet = set.Remove(key);
                return newSet.IsEmpty ? d.Remove(prop) : d.SetItem(prop, newSet);
            }
            return d;
        });
    }

    /// <summary>更新时同步迁移（属性变化时调用，传入旧值和新值对象）</summary>
    internal void Update(TKey key, TValue oldValue, TValue newValue) {
        Remove(key, oldValue);
        Add(key, newValue);
    }

    /// <summary>按属性值更新索引 — 可变属性变化时调用，从旧属性桶迁移到新属性桶</summary>
    internal void UpdateProperty(TKey key, TProperty oldProperty, TProperty newProperty) {
        if (EqualityComparer<TProperty>.Default.Equals(oldProperty, newProperty)) return;
        ImmutableInterlocked.Update(ref _index, d => {
            if (d.TryGetValue(oldProperty, out var oldSet)) {
                var newOldSet = oldSet.Remove(key);
                d = newOldSet.IsEmpty ? d.Remove(oldProperty) : d.SetItem(oldProperty, newOldSet);
            }
            var newSet = d.GetValueOrDefault(newProperty) ?? ImmutableHashSet<TKey>.Empty;
            return d.SetItem(newProperty, newSet.Add(key));
        });
    }

    /// <summary>O(1) 查询：返回指定属性值对应的所有 key</summary>
    public IReadOnlyCollection<TKey> GetKeys(TProperty property)
        => Volatile.Read(ref _index).GetValueOrDefault(property) ?? ImmutableHashSet<TKey>.Empty;

    /// <summary>O(1) 查询：返回指定属性值对应的所有 value（需传入主字典引用）</summary>
    public IEnumerable<TValue> GetValues(TProperty property, IReadOnlyDictionary<TKey, TValue> items) {
        var keys = Volatile.Read(ref _index).GetValueOrDefault(property);
        if (keys is null) return [];
        return keys.Select(k => items[k]);
    }

    /// <summary>O(1) 查询：是否存在指定属性值的项</summary>
    public bool Contains(TProperty property)
        => Volatile.Read(ref _index).ContainsKey(property);

    /// <summary>O(1) 查询：指定属性值的项数量</summary>
    public int CountByProperty(TProperty property)
        => Volatile.Read(ref _index).GetValueOrDefault(property)?.Count ?? 0;
}
