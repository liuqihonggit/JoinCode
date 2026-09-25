namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 通用字典注册器基类 — 内部 ImmutableDictionary + 无锁 CAS，对外暴露 IEnumerable（遍历器）+ IReadOnlyDictionary（字典视图）
/// 可选 Canonical/Alias 跟踪 — 子类需要区分正式名和别名时启用
/// 可选次级索引 — 子类通过 CreateIndex 声明，注册/注销自动同步，O(1) 按属性查找
/// </summary>
public class MapRegistry<TKey, TValue> where TKey : notnull {
    private ImmutableDictionary<TKey, TValue> _items;
    private ImmutableHashSet<TKey> _canonicalKeys;
    private readonly bool _trackCanonical;
    private ImmutableList<ISecondaryIndex> _indices = ImmutableList<ISecondaryIndex>.Empty;

    /// <summary>次级索引类型擦除接口 — 基类统一调用 Add/Remove 同步</summary>
    private interface ISecondaryIndex {
        /// <summary>注册时同步添加到索引</summary>
        void Add(TKey key, TValue value);
        /// <summary>注销时同步从索引移除</summary>
        void Remove(TKey key, TValue value);
    }

    /// <summary>次级索引桥接器 — 包装 SecondaryIndex 并实现 ISecondaryIndex</summary>
    private sealed class SecondaryIndexBox<TProperty> : ISecondaryIndex where TProperty : notnull {
        internal readonly SecondaryIndex<TKey, TValue, TProperty> Inner;
        internal SecondaryIndexBox(Func<TValue, TProperty> selector, IEqualityComparer<TProperty>? comparer)
            => Inner = new SecondaryIndex<TKey, TValue, TProperty>(selector, comparer);
        void ISecondaryIndex.Add(TKey key, TValue value) => Inner.Add(key, value);
        void ISecondaryIndex.Remove(TKey key, TValue value) => Inner.Remove(key, value);
    }

    /// <summary>
    /// 创建次级索引 — 子类在构造时调用，按 TValue 属性建立 O(1) 查找索引
    /// 注册/注销时基类自动同步，查询时通过 index.GetValues(property, AsDictionary()) 获取
    /// </summary>
    /// <typeparam name="TProperty">索引属性类型</typeparam>
    /// <param name="selector">属性选择器</param>
    /// <param name="comparer">属性相等比较器（可选）</param>
    protected SecondaryIndex<TKey, TValue, TProperty> CreateIndex<TProperty>(
        Func<TValue, TProperty> selector,
        IEqualityComparer<TProperty>? comparer = null) where TProperty : notnull {
        var box = new SecondaryIndexBox<TProperty>(selector, comparer);
        foreach (var kvp in Volatile.Read(ref _items))
            box.Inner.Add(kvp.Key, kvp.Value);
        ImmutableInterlocked.Update(ref _indices, list => list.Add(box));
        return box.Inner;
    }

    private void SyncIndicesAdd(TKey key, TValue value) {
        foreach (var index in Volatile.Read(ref _indices))
            index.Add(key, value);
    }

    private void SyncIndicesRemove(TKey key, TValue value) {
        foreach (var index in Volatile.Read(ref _indices))
            index.Remove(key, value);
    }

    /// <summary>
    /// 更新次级索引 — TValue 的被索引属性变化时调用，同步迁移 key 到新属性值桶
    /// </summary>
    /// <typeparam name="TProperty">索引属性类型</typeparam>
    /// <param name="index">次级索引实例</param>
    /// <param name="key">项的键</param>
    /// <param name="oldValue">变更前的旧值（用于定位旧桶）</param>
    /// <param name="newValue">变更后的新值（用于定位新桶）</param>
    protected static void Reindex<TProperty>(
        SecondaryIndex<TKey, TValue, TProperty> index,
        TKey key, TValue oldValue, TValue newValue) where TProperty : notnull
        => index.Update(key, oldValue, newValue);

    /// <summary>
    /// 更新次级索引（按属性值） — TValue 的被索引属性在原对象上变化时调用
    /// </summary>
    /// <typeparam name="TProperty">索引属性类型</typeparam>
    /// <param name="index">次级索引实例</param>
    /// <param name="key">项的键</param>
    /// <param name="oldProperty">变更前的旧属性值</param>
    /// <param name="newProperty">变更后的新属性值</param>
    protected static void Reindex<TProperty>(
        SecondaryIndex<TKey, TValue, TProperty> index,
        TKey key, TProperty oldProperty, TProperty newProperty) where TProperty : notnull
        => index.UpdateProperty(key, oldProperty, newProperty);

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
        SyncIndicesAdd(key, value);
    }

    /// <summary>注册或更新项</summary>
    protected void AddOrUpdateCore(TKey key, TValue value) {
        var old = default(TValue);
        var hadOld = false;
        ImmutableInterlocked.Update(ref _items, d => {
            if (d.TryGetValue(key, out var v)) {
                old = v;
                hadOld = true;
            }
            return d.SetItem(key, value);
        });
        if (hadOld)
            SyncIndicesRemove(key, old!);
        SyncIndicesAdd(key, value);
    }

    /// <summary>注销项</summary>
    protected bool RemoveCore(TKey key) {
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
        if (removed)
            SyncIndicesRemove(key, captured!);
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
        if (removed)
            SyncIndicesRemove(key, captured!);
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

    /// <summary>
    /// 尝试注册项 — 已存在则不覆盖，返回 false（原子操作，无锁 CAS）
    /// 对齐 ConcurrentDictionary.TryAdd 语义，用于"重复注册抛异常/忽略"场景
    /// </summary>
    public bool TryAdd(TKey key, TValue value) {
        var added = false;
        ImmutableInterlocked.Update(ref _items, d => {
            if (d.ContainsKey(key)) return d;
            added = true;
            return d.Add(key, value);
        });
        if (added)
            SyncIndicesAdd(key, value);
        return added;
    }

    /// <summary>
    /// 尝试注销项并返回被移除的值 — 原子操作，无锁 CAS
    /// 对齐 ConcurrentDictionary.TryRemove 语义，用于"移除后需释放资源"场景
    /// </summary>
    public bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value) {
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
        if (removed)
            SyncIndicesRemove(key, captured!);
        return removed;
    }

    // === Canonical/Alias 支持（trackCanonical=true 时启用）===

    /// <summary>注册项（含 Canonical 标记）</summary>
    public void Register(TKey key, TValue value, bool isCanonical = true) {
        var old = default(TValue);
        var hadOld = false;
        ImmutableInterlocked.Update(ref _items, d => {
            if (d.TryGetValue(key, out var v)) {
                old = v;
                hadOld = true;
            }
            return d.SetItem(key, value);
        });
        if (hadOld)
            SyncIndicesRemove(key, old!);
        SyncIndicesAdd(key, value);
        if (isCanonical && _trackCanonical)
            ImmutableInterlocked.Update(ref _canonicalKeys, s => s.Add(key));
    }

    /// <summary>注册别名（不覆盖已存在的项，不标记为 Canonical）</summary>
    public void RegisterAlias(TKey alias, TValue value) {
        ImmutableInterlocked.Update(ref _items, d => d.ContainsKey(alias) ? d : d.Add(alias, value));
        SyncIndicesAdd(alias, value);
    }

    /// <summary>注销项（公开方法，同时移除 Canonical 标记）</summary>
    public bool Unregister(TKey key) {
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
        if (removed) {
            SyncIndicesRemove(key, captured!);
            if (_trackCanonical)
                ImmutableInterlocked.Update(ref _canonicalKeys, s => s.Remove(key));
        }
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
