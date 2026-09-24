namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 通用字典注册器基类 — 内部 ImmutableDictionary + 无锁 CAS，对外暴露 IEnumerable（遍历器）+ IReadOnlyDictionary（字典视图）
/// 可选 Canonical/Alias 跟踪 — 子类需要区分正式名和别名时启用
/// </summary>
public class MapRegistry<TKey, TValue> where TKey : notnull {
    private ImmutableDictionary<TKey, TValue> _items;
    private ImmutableHashSet<TKey> _canonicalKeys;
    private readonly bool _trackCanonical;

    /// <summary>当前注册项总数</summary>
    public int Count => Volatile.Read(ref _items).Count;

    /// <summary>构造字典注册器。</summary>
    /// <param name="comparer">键相等比较器。</param>
    /// <param name="trackCanonical">是否跟踪正式名/别名。</param>
    public MapRegistry(IEqualityComparer<TKey>? comparer = null, bool trackCanonical = false) {
        var c = comparer ?? EqualityComparer<TKey>.Default;
        _items = ImmutableDictionary<TKey, TValue>.Empty.WithComparers(c);
        _canonicalKeys = ImmutableHashSet<TKey>.Empty.WithComparer(c);
        _trackCanonical = trackCanonical;
    }

    /// <summary>注册项（已存在则不覆盖）</summary>
    protected void AddCore(TKey key, TValue value) {
        ImmutableInterlocked.Update(ref _items, d => d.Add(key, value));
    }

    /// <summary>注册或更新项</summary>
    protected void AddOrUpdateCore(TKey key, TValue value) {
        ImmutableInterlocked.Update(ref _items, d => d.SetItem(key, value));
    }

    /// <summary>注销项</summary>
    protected bool RemoveCore(TKey key) {
        var removed = false;
        ImmutableInterlocked.Update(ref _items, d => {
            if (d.ContainsKey(key)) {
                removed = true;
                return d.Remove(key);
            }
            return d;
        });
        return removed;
    }

    /// <summary>注销项并返回被移除的值</summary>
    protected bool RemoveCore(TKey key, [MaybeNullWhen(false)] out TValue value) {
        var captured = default(TValue);
        var removed = false;
        ImmutableInterlocked.Update(ref _items, d => {
            if (d.TryGetValue(key, out var v)) {
                captured = v;
                removed = true;
                return d.Remove(key);
            }
            return d;
        });
        value = captured;
        return removed;
    }

    /// <summary>按键获取（O(1)）</summary>
    public TValue? Get(TKey key) => Volatile.Read(ref _items).GetValueOrDefault(key);

    /// <summary>按键尝试获取（O(1)）</summary>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        => Volatile.Read(ref _items).TryGetValue(key, out value);

    /// <summary>是否包含指定键</summary>
    public bool ContainsKey(TKey key) => Volatile.Read(ref _items).ContainsKey(key);

    /// <summary>
    /// 遍历器 — 返回不可变 Values，不分配新集合，调用方可安全遍历无需 ToList 快照
    /// </summary>
    public IEnumerable<TValue> GetAll() => Volatile.Read(ref _items).Values;

    /// <summary>
    /// 键值对遍历器 — 返回不可变字典枚举，不分配新集合
    /// </summary>
    protected IEnumerable<KeyValuePair<TKey, TValue>> EntriesCore => Volatile.Read(ref _items);

    /// <summary>
    /// 字典视图 — 直接返回不可变字典引用，不分配新集合
    /// 调用方可按键查找 + 遍历，线程安全
    /// </summary>
    public IReadOnlyDictionary<TKey, TValue> AsDictionary() => Volatile.Read(ref _items);

    /// <summary>条件过滤遍历器 — 不分配新集合</summary>
    public IEnumerable<TValue> Where(Func<TValue, bool> predicate)
        => Volatile.Read(ref _items).Values.Where(predicate);

    /// <summary>清空所有注册（测试用）</summary>
    public void Clear() {
        var old = Interlocked.Exchange(ref _items, ImmutableDictionary<TKey, TValue>.Empty.WithComparers(Volatile.Read(ref _items).KeyComparer));
        if (_trackCanonical)
            Interlocked.Exchange(ref _canonicalKeys, ImmutableHashSet<TKey>.Empty.WithComparer(Volatile.Read(ref _canonicalKeys).KeyComparer));
    }

    /// <summary>清空所有注册并返回被清空的项（子类需要在清空前执行清理逻辑时使用）</summary>
    protected List<KeyValuePair<TKey, TValue>> ClearCore() {
        var old = Interlocked.Exchange(ref _items, ImmutableDictionary<TKey, TValue>.Empty.WithComparers(Volatile.Read(ref _items).KeyComparer));
        if (_trackCanonical)
            Interlocked.Exchange(ref _canonicalKeys, ImmutableHashSet<TKey>.Empty.WithComparer(Volatile.Read(ref _canonicalKeys).KeyComparer));
        return [.. old];
    }

    // === Canonical/Alias 支持（trackCanonical=true 时启用）===

    /// <summary>注册项（含 Canonical 标记）</summary>
    public void Register(TKey key, TValue value, bool isCanonical = true) {
        ImmutableInterlocked.Update(ref _items, d => d.SetItem(key, value));
        if (isCanonical && _trackCanonical)
            ImmutableInterlocked.Update(ref _canonicalKeys, s => s.Add(key));
    }

    /// <summary>注册别名（不覆盖已存在的项，不标记为 Canonical）</summary>
    public void RegisterAlias(TKey alias, TValue value) {
        ImmutableInterlocked.Update(ref _items, d => d.Add(alias, value));
    }

    /// <summary>注销项（公开方法，同时移除 Canonical 标记）</summary>
    public bool Unregister(TKey key) {
        var removed = false;
        ImmutableInterlocked.Update(ref _items, d => {
            if (d.ContainsKey(key)) {
                removed = true;
                return d.Remove(key);
            }
            return d;
        });
        if (_trackCanonical && removed)
            ImmutableInterlocked.Update(ref _canonicalKeys, s => s.Remove(key));
        return removed;
    }

    /// <summary>获取所有 Canonical 项的字典视图 — 从不可变快照构建 FrozenDictionary</summary>
    public IReadOnlyDictionary<TKey, TValue> GetAllCanonical() {
        if (!_trackCanonical)
            return Volatile.Read(ref _items);
        var canonical = Volatile.Read(ref _canonicalKeys);
        var items = Volatile.Read(ref _items);
        return canonical.Where(n => items.ContainsKey(n))
            .ToFrozenDictionary(n => n, n => items[n]);
    }

    /// <summary>获取所有 Canonical 项的键值对遍历器</summary>
    public IEnumerable<KeyValuePair<TKey, TValue>> GetCanonicalEntries() {
        if (!_trackCanonical)
            return Volatile.Read(ref _items);
        var canonical = Volatile.Read(ref _canonicalKeys);
        var items = Volatile.Read(ref _items);
        return canonical.Where(n => items.ContainsKey(n))
            .Select(n => new KeyValuePair<TKey, TValue>(n, items[n]));
    }
}
