namespace Structura.Collections;

internal readonly struct Unit {
    public static readonly Unit Default = default;
    public override bool Equals(object? obj) => obj is Unit;
    public override int GetHashCode() => 0;
}

/// <summary>ImmutableHamTSet 工厂入口 — 提供 Create&lt;T&gt;() 等泛型工厂方法,与 ImmutableHashSet API 完全对齐。</summary>
public static class ImmutableHamTSet {
    /// <summary>创建空集合。</summary>
    public static ImmutableHamTSet<T> Create<T>() where T : notnull
        => ImmutableHamTSet<T>.Create();

    /// <summary>创建空集合,使用指定比较器。</summary>
    public static ImmutableHamTSet<T> Create<T>(IEqualityComparer<T>? comparer) where T : notnull
        => ImmutableHamTSet<T>.Create(comparer);

    /// <summary>从序列创建集合。</summary>
    public static ImmutableHamTSet<T> CreateRange<T>(IEnumerable<T> items) where T : notnull
        => ImmutableHamTSet<T>.CreateRange(items);

    /// <summary>从序列创建集合,使用指定比较器。</summary>
    public static ImmutableHamTSet<T> CreateRange<T>(IEnumerable<T> items, IEqualityComparer<T>? comparer) where T : notnull
        => ImmutableHamTSet<T>.CreateRange(items, comparer);

    /// <summary>创建可变构建器。</summary>
    public static ImmutableHamTSet<T>.Builder CreateBuilder<T>() where T : notnull
        => ImmutableHamTSet<T>.CreateBuilder();

    /// <summary>创建可变构建器,使用指定比较器。</summary>
    public static ImmutableHamTSet<T>.Builder CreateBuilder<T>(IEqualityComparer<T>? comparer) where T : notnull
        => ImmutableHamTSet<T>.CreateBuilder(comparer);
}

/// <summary>不可变 HAMT 集合 — 基于 ImmutableHamT&lt;T, Unit&gt; 包装,查找 O(log₃₂ N)。不可变 + 路径复制,配合 Interlocked.Exchange 实现无锁 CAS。</summary>
public sealed class ImmutableHamTSet<T> : IReadOnlySet<T> where T : notnull {
    private readonly ImmutableHamT<T, Unit> _map;

    private ImmutableHamTSet(ImmutableHamT<T, Unit> map) => _map = map;

    /// <summary>空集合,使用默认相等比较器。</summary>
    public static ImmutableHamTSet<T> Empty { get; } = new(ImmutableHamT<T, Unit>.Empty);

    /// <summary>当前元素数量。</summary>
    public int Count => _map.Count;

    /// <summary>是否为空。</summary>
    public bool IsEmpty => _map.IsEmpty;

    /// <summary>当前相等比较器。</summary>
    public IEqualityComparer<T> Comparer => _map.KeyComparer;

    /// <summary>当前相等比较器(对齐 ImmutableHashSet.KeyComparer API)。</summary>
    public IEqualityComparer<T> KeyComparer => _map.KeyComparer;

    /// <summary>创建空集合,使用指定比较器。</summary>
    public static ImmutableHamTSet<T> Create(IEqualityComparer<T>? comparer = null)
        => new(ImmutableHamT<T, Unit>.Create(comparer));

    /// <summary>从序列创建集合。</summary>
    public static ImmutableHamTSet<T> CreateRange(IEnumerable<T> items, IEqualityComparer<T>? comparer = null) {
        var result = Create(comparer);
        foreach (var item in items) result = result.Add(item);
        return result;
    }

    /// <summary>创建可变构建器。</summary>
    public static Builder CreateBuilder(IEqualityComparer<T>? comparer = null)
        => new(comparer ?? EqualityComparer<T>.Default);

    /// <summary>添加元素,已存在则返回原实例。单次查找(委托 TryAddInternal,消除 ContainsKey+SetItem 双查找)。</summary>
    public ImmutableHamTSet<T> Add(T item) {
        var newMap = _map.TryAddInternal(item, Unit.Default, out var added);
        return added ? new(newMap) : this;
    }

    /// <summary>移除元素,不存在则返回原实例。</summary>
    public ImmutableHamTSet<T> Remove(T item) {
        var newMap = _map.Remove(item);
        return ReferenceEquals(newMap, _map) ? this : new(newMap);
    }

    /// <summary>是否包含指定元素。</summary>
    public bool Contains(T item) => _map.ContainsKey(item);

    /// <summary>清空所有元素。</summary>
    public ImmutableHamTSet<T> Clear() => new(_map.Clear());

    /// <summary>批量添加。</summary>
    public ImmutableHamTSet<T> AddRange(IEnumerable<T> items) {
        var result = this;
        foreach (var item in items) result = result.Add(item);
        return result;
    }

    /// <summary>批量移除。</summary>
    public ImmutableHamTSet<T> RemoveRange(IEnumerable<T> items) {
        var result = this;
        foreach (var item in items) result = result.Remove(item);
        return result;
    }

    /// <summary>使用指定比较器重建实例。</summary>
    public ImmutableHamTSet<T> WithComparer(IEqualityComparer<T> comparer) {
        if (comparer == _map.KeyComparer) return this;
        var result = Create(comparer);
        foreach (var item in this) result = result.Add(item);
        return result;
    }

    /// <summary>获取可变构建器副本。</summary>
    public Builder ToBuilder() {
        var b = new Builder(_map.KeyComparer);
        foreach (var item in this) b.Add(item);
        return b;
    }

    /// <summary>遍历所有元素 — 委托 ImmutableHamT.EnumerateKeys 显式栈遍历,消除额外 yield 包装层。</summary>
    public IEnumerator<T> GetEnumerator() => _map.EnumerateKeys().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>是否与另一集合有交集。</summary>
    public bool Overlaps(IEnumerable<T> other) {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var item in other)
            if (Contains(item)) return true;
        return false;
    }

    /// <summary>是否是另一集合的子集。</summary>
    public bool IsSubsetOf(IEnumerable<T> other) {
        ArgumentNullException.ThrowIfNull(other);
        var otherSet = BuildOtherSet(other);
        foreach (var item in this)
            if (!otherSet.Contains(item)) return false;
        return true;
    }

    /// <summary>是否是另一集合的超集。</summary>
    public bool IsSupersetOf(IEnumerable<T> other) {
        ArgumentNullException.ThrowIfNull(other);
        foreach (var item in other)
            if (!Contains(item)) return false;
        return true;
    }

    /// <summary>是否是另一集合的真子集。</summary>
    public bool IsProperSubsetOf(IEnumerable<T> other) {
        ArgumentNullException.ThrowIfNull(other);
        var otherSet = BuildOtherSet(other);
        if (Count >= otherSet.Count) return false;
        return IsSubsetOf(otherSet);
    }

    /// <summary>是否是另一集合的真超集。</summary>
    public bool IsProperSupersetOf(IEnumerable<T> other) {
        ArgumentNullException.ThrowIfNull(other);
        var otherSet = BuildOtherSet(other);
        if (Count <= otherSet.Count) return false;
        return IsSupersetOf(otherSet);
    }

    /// <summary>是否与另一集合包含相同元素。</summary>
    public bool SetEquals(IEnumerable<T> other) {
        ArgumentNullException.ThrowIfNull(other);
        var otherSet = BuildOtherSet(other);
        if (Count != otherSet.Count) return false;
        foreach (var item in this)
            if (!otherSet.Contains(item)) return false;
        return true;
    }

    private HashSet<T> BuildOtherSet(IEnumerable<T> other) {
        if (other is ImmutableHamTSet<T> hamtSet) return new(hamtSet, hamtSet.Comparer);
        if (other is IReadOnlySet<T> roSet) return new(roSet, Comparer);
        return new(other, Comparer);
    }

    /// <summary>可变构建器 — 内部用 HashSet&lt;T&gt;,ToImmutable 时 O(N) 重建。</summary>
    public sealed class Builder : ISet<T> {
        private readonly HashSet<T> _set;

        internal Builder(IEqualityComparer<T> comparer) => _set = new(comparer);

        /// <summary>构建不可变集合。</summary>
        public ImmutableHamTSet<T> ToImmutable() {
            var result = ImmutableHamTSet<T>.Create(_set.Comparer);
            foreach (var item in _set) result = result.Add(item);
            return result;
        }

        /// <summary>元素数量。</summary>
        public int Count => _set.Count;

        /// <summary>是否只读。</summary>
        public bool IsReadOnly => false;

        /// <summary>添加元素,返回是否新增成功。</summary>
        public bool Add(T item) => _set.Add(item);

        void ICollection<T>.Add(T item) => _set.Add(item);

        /// <summary>移除元素。</summary>
        public bool Remove(T item) => _set.Remove(item);

        /// <summary>清空。</summary>
        public void Clear() => _set.Clear();

        /// <summary>是否包含元素。</summary>
        public bool Contains(T item) => _set.Contains(item);

        /// <summary>复制到数组。</summary>
        public void CopyTo(T[] array, int arrayIndex) => _set.CopyTo(array, arrayIndex);

        /// <summary>遍历。</summary>
        public IEnumerator<T> GetEnumerator() => _set.GetEnumerator();

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>排除另一集合中的所有元素。</summary>
        public void ExceptWith(IEnumerable<T> other) => _set.ExceptWith(other);

        /// <summary>仅保留与另一集合的交集。</summary>
        public void IntersectWith(IEnumerable<T> other) => _set.IntersectWith(other);

        /// <summary>合并另一集合的所有元素。</summary>
        public void UnionWith(IEnumerable<T> other) => _set.UnionWith(other);

        /// <summary>对称差集:仅保留恰好在一个集合中的元素。</summary>
        public void SymmetricExceptWith(IEnumerable<T> other) => _set.SymmetricExceptWith(other);

        /// <summary>是否是另一集合的子集。</summary>
        public bool IsSubsetOf(IEnumerable<T> other) => _set.IsSubsetOf(other);

        /// <summary>是否是另一集合的超集。</summary>
        public bool IsSupersetOf(IEnumerable<T> other) => _set.IsSupersetOf(other);

        /// <summary>是否是另一集合的真子集。</summary>
        public bool IsProperSubsetOf(IEnumerable<T> other) => _set.IsProperSubsetOf(other);

        /// <summary>是否是另一集合的真超集。</summary>
        public bool IsProperSupersetOf(IEnumerable<T> other) => _set.IsProperSupersetOf(other);

        /// <summary>是否与另一集合有交集。</summary>
        public bool Overlaps(IEnumerable<T> other) => _set.Overlaps(other);

        /// <summary>是否与另一集合包含相同元素。</summary>
        public bool SetEquals(IEnumerable<T> other) => _set.SetEquals(other);
    }
}

/// <summary>ImmutableHamTSet LINQ 扩展方法 — 对齐 ToImmutableHashSet API。</summary>
public static class ImmutableHamTSetExtensions {
    /// <summary>将序列转换为 ImmutableHamTSet。</summary>
    public static ImmutableHamTSet<T> ToImmutableHamTSet<T>(this IEnumerable<T> source) where T : notnull {
        var result = ImmutableHamTSet<T>.Empty;
        foreach (var item in source) result = result.Add(item);
        return result;
    }

    /// <summary>将序列转换为 ImmutableHamTSet,使用指定比较器。</summary>
    public static ImmutableHamTSet<T> ToImmutableHamTSet<T>(this IEnumerable<T> source, IEqualityComparer<T> comparer) where T : notnull {
        var result = ImmutableHamTSet<T>.Create(comparer);
        foreach (var item in source) result = result.Add(item);
        return result;
    }
}
