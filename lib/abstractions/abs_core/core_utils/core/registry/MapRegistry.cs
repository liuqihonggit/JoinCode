// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 通用字典注册器基类 — 内部 ImmutableDictionary + 无锁 CAS，对外暴露 IEnumerable（遍历器）+ IReadOnlyDictionary（字典视图）
/// 可选 Canonical/Alias 跟踪 — 子类需要区分正式名和别名时启用
/// 可选次级索引 — 子类通过 CreateIndex 声明，注册/注销自动同步，O(1) 按属性查找
/// 手写 CAS 循环替代 ImmutableInterlocked.Update/Interlocked.Exchange(volatile 字段触发 CS0420)
/// </summary>
public class MapRegistry<TKey, TValue> where TKey : notnull {
    private volatile ImmutableHamT<TKey, TValue> _items;
    private volatile ImmutableHamTSet<TKey> _canonicalKeys;
    private readonly bool _trackCanonical;
    private volatile ImmutableList<ISecondaryIndex> _indices = ImmutableList<ISecondaryIndex>.Empty;

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
        foreach (var kvp in _items)
            box.Inner.Add(kvp.Key, kvp.Value);
        while (true) {
            var current = _indices;
            var updated = current.Add(box);
            if (Interlocked.CompareExchange(ref _indices, updated, current) == current) break;
        }
        return box.Inner;
    }

    private void SyncIndicesAdd(TKey key, TValue value) {
        foreach (var index in _indices)
            index.Add(key, value);
    }

    private void SyncIndicesRemove(TKey key, TValue value) {
        foreach (var index in _indices)
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
    public int Count => _items.Count;

    /// <summary>构造字典注册器。</summary>
    /// <param name="comparer">键相等比较器。</param>
    /// <param name="trackCanonical">是否跟踪正式名/别名。</param>
    public MapRegistry(IEqualityComparer<TKey>? comparer = null, bool trackCanonical = false) {
        var c = comparer ?? EqualityComparer<TKey>.Default;
        _items = ImmutableHamT<TKey, TValue>.Empty.WithComparers(c);
        _canonicalKeys = ImmutableHamTSet<TKey>.Empty.WithComparer(c);
        _trackCanonical = trackCanonical;
    }

    /// <summary>注册项（已存在则不覆盖）— 手写 CAS 循环替代 ImmutableInterlocked.Update。</summary>
    protected void AddCore(TKey key, TValue value) {
        while (true) {
            var current = _items;
            var updated = current.Add(key, value);
            if (Interlocked.CompareExchange(ref _items, updated, current) == current) break;
        }
        SyncIndicesAdd(key, value);
    }

    /// <summary>注册或更新项 — 手写 CAS 循环替代 ImmutableInterlocked.Update。</summary>
    protected void AddOrUpdateCore(TKey key, TValue value) {
        var old = default(TValue);
        var hadOld = false;
        while (true) {
            var current = _items;
            if (current.TryGetValue(key, out var v)) {
                old = v;
                hadOld = true;
            }
            var updated = current.SetItem(key, value);
            if (Interlocked.CompareExchange(ref _items, updated, current) == current) break;
        }
        if (hadOld)
            SyncIndicesRemove(key, old!);
        SyncIndicesAdd(key, value);
    }

    /// <summary>注销项 — 手写 CAS 循环替代 ImmutableInterlocked.Update。</summary>
    protected bool RemoveCore(TKey key) {
        var captured = default(TValue);
        var removed = false;
        while (true) {
            var current = _items;
            if (!current.TryGetValue(key, out var v)) break;
            var updated = current.Remove(key);
            if (Interlocked.CompareExchange(ref _items, updated, current) == current) { captured = v; removed = true; break; }
        }
        if (removed)
            SyncIndicesRemove(key, captured!);
        return removed;
    }

    /// <summary>注销项并返回被移除的值 — 手写 CAS 循环替代 ImmutableInterlocked.Update。</summary>
    protected bool RemoveCore(TKey key, [MaybeNullWhen(false)] out TValue value) {
        var captured = default(TValue);
        var removed = false;
        while (true) {
            var current = _items;
            if (!current.TryGetValue(key, out var v)) break;
            var updated = current.Remove(key);
            if (Interlocked.CompareExchange(ref _items, updated, current) == current) { captured = v; removed = true; break; }
        }
        value = captured;
        if (removed)
            SyncIndicesRemove(key, captured!);
        return removed;
    }

    /// <summary>按键获取（O(1)）</summary>
    public TValue? Get(TKey key) => _items.GetValueOrDefault(key);

    /// <summary>按键尝试获取（O(1)）</summary>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        => _items.TryGetValue(key, out value);

    /// <summary>是否包含指定键</summary>
    public bool ContainsKey(TKey key) => _items.ContainsKey(key);

    /// <summary>
    /// 遍历器 — 返回不可变 Values，不分配新集合，调用方可安全遍历无需 ToList 快照
    /// </summary>
    public IEnumerable<TValue> GetAll() => _items.Values;

    /// <summary>
    /// 键值对遍历器 — 返回不可变字典枚举，不分配新集合
    /// </summary>
    protected IEnumerable<KeyValuePair<TKey, TValue>> EntriesCore => _items;

    /// <summary>
    /// 字典视图 — 直接返回不可变字典引用，不分配新集合
    /// 调用方可按键查找 + 遍历，线程安全
    /// </summary>
    public IReadOnlyDictionary<TKey, TValue> AsDictionary() => _items;

    /// <summary>条件过滤遍历器 — 不分配新集合</summary>
    public IEnumerable<TValue> Where(Func<TValue, bool> predicate)
        => _items.Values.Where(predicate);

    /// <summary>清空所有注册（测试用）— 手写 CAS 循环替代 Interlocked.Exchange,保留 comparer 和旧值。</summary>
    public void Clear() {
        while (true) {
            var current = _items;
            if (Interlocked.CompareExchange(ref _items, ImmutableHamT<TKey, TValue>.Empty.WithComparers(current.KeyComparer), current) == current) break;
        }
        if (_trackCanonical) {
            while (true) {
                var current = _canonicalKeys;
                if (Interlocked.CompareExchange(ref _canonicalKeys, ImmutableHamTSet<TKey>.Empty.WithComparer(current.KeyComparer), current) == current) break;
            }
        }
    }

    /// <summary>清空所有注册并返回被清空的项 — 手写 CAS 循环替代 Interlocked.Exchange,保留 comparer 和旧值。</summary>
    protected List<KeyValuePair<TKey, TValue>> ClearCore() {
        ImmutableHamT<TKey, TValue> old = default!;
        while (true) {
            var current = _items;
            if (Interlocked.CompareExchange(ref _items, ImmutableHamT<TKey, TValue>.Empty.WithComparers(current.KeyComparer), current) == current) { old = current; break; }
        }
        if (_trackCanonical) {
            while (true) {
                var current = _canonicalKeys;
                if (Interlocked.CompareExchange(ref _canonicalKeys, ImmutableHamTSet<TKey>.Empty.WithComparer(current.KeyComparer), current) == current) break;
            }
        }
        return [.. old];
    }

    /// <summary>
    /// 尝试注册项 — 已存在则不覆盖，返回 false（原子操作，无锁 CAS）
    /// 对齐 ConcurrentDictionary.TryAdd 语义，用于"重复注册抛异常/忽略"场景
    /// 手写 CAS 循环替代 ImmutableInterlocked.Update
    /// </summary>
    public bool TryAdd(TKey key, TValue value) {
        var added = false;
        while (true) {
            var current = _items;
            if (current.ContainsKey(key)) break;
            var updated = current.Add(key, value);
            if (Interlocked.CompareExchange(ref _items, updated, current) == current) { added = true; break; }
        }
        if (added)
            SyncIndicesAdd(key, value);
        return added;
    }

    /// <summary>
    /// 尝试注销项并返回被移除的值 — 原子操作，无锁 CAS
    /// 对齐 ConcurrentDictionary.TryRemove 语义，用于"移除后需释放资源"场景
    /// 手写 CAS 循环替代 ImmutableInterlocked.Update
    /// </summary>
    public bool TryRemove(TKey key, [MaybeNullWhen(false)] out TValue value) {
        var captured = default(TValue);
        var removed = false;
        while (true) {
            var current = _items;
            if (!current.TryGetValue(key, out var v)) break;
            var updated = current.Remove(key);
            if (Interlocked.CompareExchange(ref _items, updated, current) == current) { captured = v; removed = true; break; }
        }
        value = captured;
        if (removed)
            SyncIndicesRemove(key, captured!);
        return removed;
    }

    // === Canonical/Alias 支持（trackCanonical=true 时启用）===

    /// <summary>注册项（含 Canonical 标记）— 手写 CAS 循环替代 ImmutableInterlocked.Update。</summary>
    public void Register(TKey key, TValue value, bool isCanonical = true) {
        var old = default(TValue);
        var hadOld = false;
        while (true) {
            var current = _items;
            if (current.TryGetValue(key, out var v)) {
                old = v;
                hadOld = true;
            }
            var updated = current.SetItem(key, value);
            if (Interlocked.CompareExchange(ref _items, updated, current) == current) break;
        }
        if (hadOld)
            SyncIndicesRemove(key, old!);
        SyncIndicesAdd(key, value);
        if (isCanonical && _trackCanonical) {
            while (true) {
                var current = _canonicalKeys;
                var updated = current.Add(key);
                if (Interlocked.CompareExchange(ref _canonicalKeys, updated, current) == current) break;
            }
        }
    }

    /// <summary>注册别名（不覆盖已存在的项，不标记为 Canonical）— 手写 CAS 循环替代 ImmutableInterlocked.Update。</summary>
    public void RegisterAlias(TKey alias, TValue value) {
        while (true) {
            var current = _items;
            if (current.ContainsKey(alias)) break;
            var updated = current.Add(alias, value);
            if (Interlocked.CompareExchange(ref _items, updated, current) == current) break;
        }
        SyncIndicesAdd(alias, value);
    }

    /// <summary>注销项（公开方法，同时移除 Canonical 标记）— 手写 CAS 循环替代 ImmutableInterlocked.Update。</summary>
    public bool Unregister(TKey key) {
        var captured = default(TValue);
        var removed = false;
        while (true) {
            var current = _items;
            if (!current.TryGetValue(key, out var v)) break;
            var updated = current.Remove(key);
            if (Interlocked.CompareExchange(ref _items, updated, current) == current) { captured = v; removed = true; break; }
        }
        if (!removed) return false;
        SyncIndicesRemove(key, captured!);
        if (!_trackCanonical) return true;
        while (true) {
            var current = _canonicalKeys;
            var updated = current.Remove(key);
            if (Interlocked.CompareExchange(ref _canonicalKeys, updated, current) == current) break;
        }
        return true;
    }

    /// <summary>获取所有 Canonical 项的字典视图 — 从不可变快照构建 FrozenDictionary</summary>
    public IReadOnlyDictionary<TKey, TValue> GetAllCanonical() {
        if (!_trackCanonical)
            return _items;
        var canonical = _canonicalKeys;
        var items = _items;
        return canonical.Where(n => items.ContainsKey(n))
            .ToFrozenDictionary(n => n, n => items[n]);
    }

    /// <summary>获取所有 Canonical 项的键值对遍历器</summary>
    public IEnumerable<KeyValuePair<TKey, TValue>> GetCanonicalEntries() {
        if (!_trackCanonical)
            return _items;
        var canonical = _canonicalKeys;
        var items = _items;
        return canonical.Where(n => items.ContainsKey(n))
            .Select(n => new KeyValuePair<TKey, TValue>(n, items[n]));
    }
}
