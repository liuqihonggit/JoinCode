namespace Structura.Collections;

/// <summary>ImmutableHamT 工厂入口 — 提供 Create&lt;TKey,TValue&gt;() 等泛型工厂方法,与 ImmutableDictionary API 完全对齐。</summary>
public static class ImmutableHamT {
    /// <summary>创建空实例。</summary>
    public static ImmutableHamT<TKey, TValue> Create<TKey, TValue>() where TKey : notnull
        => ImmutableHamT<TKey, TValue>.Create();

    /// <summary>创建空实例,使用指定键比较器。</summary>
    public static ImmutableHamT<TKey, TValue> Create<TKey, TValue>(IEqualityComparer<TKey>? keyComparer) where TKey : notnull
        => ImmutableHamT<TKey, TValue>.Create(keyComparer);

    /// <summary>从键值对序列创建实例。</summary>
    public static ImmutableHamT<TKey, TValue> CreateRange<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> items) where TKey : notnull
        => ImmutableHamT<TKey, TValue>.CreateRange(items);

    /// <summary>从键值对序列创建实例,使用指定键比较器。</summary>
    public static ImmutableHamT<TKey, TValue> CreateRange<TKey, TValue>(IEnumerable<KeyValuePair<TKey, TValue>> items, IEqualityComparer<TKey>? keyComparer) where TKey : notnull
        => ImmutableHamT<TKey, TValue>.CreateRange(items, keyComparer);

    /// <summary>创建可变构建器。</summary>
    public static ImmutableHamT<TKey, TValue>.Builder CreateBuilder<TKey, TValue>() where TKey : notnull
        => ImmutableHamT<TKey, TValue>.CreateBuilder();

    /// <summary>创建可变构建器,使用指定键比较器。</summary>
    public static ImmutableHamT<TKey, TValue>.Builder CreateBuilder<TKey, TValue>(IEqualityComparer<TKey>? keyComparer) where TKey : notnull
        => ImmutableHamT<TKey, TValue>.CreateBuilder(keyComparer);
}

/// <summary>不可变 HAMT (Hash Array Mapped Trie) — 分支因子 32,查找 O(log₃₂ N)。不可变 + 路径复制,配合 ImmutableInterlocked.Update 实现无锁 CAS。</summary>
public sealed class ImmutableHamT<TKey, TValue> : IReadOnlyDictionary<TKey, TValue> where TKey : notnull {
    internal const int Bits = 5;
    internal const int Width = 1 << Bits;
    internal const int Mask = Width - 1;
    internal const int MaxBitmapSize = Width / 2;
    internal const int MinArraySize = Width / 4;

    private readonly int _count;
    private readonly Node? _root;
    private readonly IEqualityComparer<TKey> _keyComparer;

    private ImmutableHamT(int count, Node? root, IEqualityComparer<TKey> comparer) {
        _count = count;
        _root = root;
        _keyComparer = comparer;
    }

    /// <summary>空 HAMT,使用默认相等比较器。</summary>
    public static ImmutableHamT<TKey, TValue> Empty { get; } = new(0, null, EqualityComparer<TKey>.Default);

    /// <summary>当前元素数量。</summary>
    public int Count => _count;

    /// <summary>是否为空。</summary>
    public bool IsEmpty => _count == 0;

    /// <summary>使用指定相等比较器重建实例。</summary>
    public ImmutableHamT<TKey, TValue> WithComparer(IEqualityComparer<TKey> comparer) {
        if (comparer == _keyComparer) return this;
        var result = new ImmutableHamT<TKey, TValue>(0, null, comparer);
        foreach (var kv in this) result = result.SetItem(kv.Key, kv.Value);
        return result;
    }

    /// <summary>使用指定键比较器重建实例(对齐 ImmutableDictionary.WithComparers API)。</summary>
    public ImmutableHamT<TKey, TValue> WithComparers(IEqualityComparer<TKey> keyComparer) => WithComparer(keyComparer);

    /// <summary>当前键比较器。</summary>
    public IEqualityComparer<TKey> KeyComparer => _keyComparer;

    /// <summary>尝试获取指定键的值。</summary>
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) {
        ArgumentNullException.ThrowIfNull(key);
        if (_root is null) { value = default; return false; }
        var hash = (uint)_keyComparer.GetHashCode(key);
        return _root.TryGet(0, hash, _keyComparer, key, out value);
    }

    /// <summary>是否包含指定键。</summary>
    public bool ContainsKey(TKey key) => TryGetValue(key, out _);

    /// <summary>尝试添加键值对,键已存在返回 false,键不存在返回 true,不抛异常。比 BCL ImmutableDictionary.TryAdd 更优:BCL 在键不存在时抛 NotSupportedException,本方法始终不抛。不可变字典不实际修改,调用方需用 Add 获取新实例。</summary>
    public bool TryAdd(TKey key, TValue value) => !ContainsKey(key);

    /// <summary>获取指定键的值,不存在抛 KeyNotFoundException。</summary>
    public TValue this[TKey key] {
        get {
            if (!TryGetValue(key, out var value))
                throw new KeyNotFoundException($"Key not found: {key}");
            return value;
        }
    }

    /// <summary>添加键值对,键已存在抛 ArgumentException。</summary>
    public ImmutableHamT<TKey, TValue> Add(TKey key, TValue value) {
        ArgumentNullException.ThrowIfNull(key);
        var hash = (uint)_keyComparer.GetHashCode(key);
        if (_root is null) return new ImmutableHamT<TKey, TValue>(1, new LeafNode(key, value), _keyComparer);
        var newRoot = _root.Add(0, hash, _keyComparer, key, value, out var added);
        if (!added) throw new ArgumentException($"An entry with the same key already exists: {key}");
        return new ImmutableHamT<TKey, TValue>(_count + 1, newRoot, _keyComparer);
    }

    /// <summary>设置键值对,键已存在则替换值。</summary>
    public ImmutableHamT<TKey, TValue> SetItem(TKey key, TValue value) {
        ArgumentNullException.ThrowIfNull(key);
        var hash = (uint)_keyComparer.GetHashCode(key);
        if (_root is null) return new ImmutableHamT<TKey, TValue>(1, new LeafNode(key, value), _keyComparer);
        var newRoot = _root.Add(0, hash, _keyComparer, key, value, out var added);
        return new ImmutableHamT<TKey, TValue>(added ? _count + 1 : _count, newRoot, _keyComparer);
    }

    /// <summary>移除指定键,不存在则返回原实例。</summary>
    public ImmutableHamT<TKey, TValue> Remove(TKey key) {
        ArgumentNullException.ThrowIfNull(key);
        if (_root is null) return this;
        var hash = (uint)_keyComparer.GetHashCode(key);
        var newRoot = _root.Remove(0, hash, _keyComparer, key, out var removed);
        if (!removed) return this;
        return new ImmutableHamT<TKey, TValue>(_count - 1, newRoot, _keyComparer);
    }

    /// <summary>批量移除指定键。</summary>
    public ImmutableHamT<TKey, TValue> RemoveRange(IEnumerable<TKey> keys) {
        var result = this;
        foreach (var key in keys) result = result.Remove(key);
        return result;
    }

    /// <summary>清空所有元素。</summary>
    public ImmutableHamT<TKey, TValue> Clear() => new(0, null, _keyComparer);

    /// <summary>批量添加,键已存在抛 ArgumentException。</summary>
    public ImmutableHamT<TKey, TValue> AddRange(IEnumerable<KeyValuePair<TKey, TValue>> items) {
        var result = this;
        foreach (var kv in items) result = result.Add(kv.Key, kv.Value);
        return result;
    }

    /// <summary>批量设置,键已存在则替换值。</summary>
    public ImmutableHamT<TKey, TValue> SetItems(IEnumerable<KeyValuePair<TKey, TValue>> items) {
        var result = this;
        foreach (var kv in items) result = result.SetItem(kv.Key, kv.Value);
        return result;
    }

    /// <summary>遍历所有键值对 — 显式栈迭代,消除嵌套 yield return 状态机开销。</summary>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() {
        if (_root is null) yield break;
        var stack = new Stack<Node>(8);
        stack.Push(_root);
        while (stack.Count > 0) {
            var node = stack.Pop();
            switch (node) {
                case LeafNode leaf:
                yield return new(leaf.Key, leaf.Value);
                break;
                case CollisionNode collision:
                for (var i = 0; i < collision.Entries.Length; i++)
                    yield return new(collision.Entries[i].Key, collision.Entries[i].Value);
                break;
                case BitmapNode bitmap:
                for (var i = bitmap.Children.Length - 1; i >= 0; i--)
                    stack.Push(bitmap.Children[i]);
                break;
                case ArrayNode array:
                for (var i = Width - 1; i >= 0; i--) {
                    var child = array.Children[i];
                    if (child is not null) stack.Push(child);
                }
                break;
            }
        }
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>仅遍历键 — 显式栈迭代,供 ImmutableHamTSet 复用以消除额外 yield 包装层。</summary>
    public IEnumerable<TKey> EnumerateKeys() {
        if (_root is null) yield break;
        var stack = new Stack<Node>(8);
        stack.Push(_root);
        while (stack.Count > 0) {
            var node = stack.Pop();
            switch (node) {
                case LeafNode leaf:
                yield return leaf.Key;
                break;
                case CollisionNode collision:
                for (var i = 0; i < collision.Entries.Length; i++)
                    yield return collision.Entries[i].Key;
                break;
                case BitmapNode bitmap:
                for (var i = bitmap.Children.Length - 1; i >= 0; i--)
                    stack.Push(bitmap.Children[i]);
                break;
                case ArrayNode array:
                for (var i = Width - 1; i >= 0; i--) {
                    var child = array.Children[i];
                    if (child is not null) stack.Push(child);
                }
                break;
            }
        }
    }

    /// <summary>所有键。</summary>
    public IEnumerable<TKey> Keys => EnumerateKeys();

    /// <summary>所有值。</summary>
    public IEnumerable<TValue> Values { get { foreach (var kv in this) yield return kv.Value; } }

    /// <summary>是否包含指定键值对。</summary>
    public bool Contains(KeyValuePair<TKey, TValue> pair) =>
        TryGetValue(pair.Key, out var value) && EqualityComparer<TValue>.Default.Equals(value, pair.Value);

    internal abstract class Node {
        internal abstract bool TryGet(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, [MaybeNullWhen(false)] out TValue value);
        internal abstract Node Add(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, TValue value, out bool added);
        internal abstract Node? Remove(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, out bool removed);
        internal abstract IEnumerable<KeyValuePair<TKey, TValue>> Enumerate();
    }

    internal sealed class LeafNode : Node {
        internal readonly TKey Key;
        internal readonly TValue Value;
        internal LeafNode(TKey key, TValue value) { Key = key; Value = value; }

        internal override bool TryGet(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, [MaybeNullWhen(false)] out TValue value) {
            if (cmp.Equals(Key, key)) { value = Value; return true; }
            value = default; return false;
        }

        internal override Node Add(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, TValue value, out bool added) {
            if (cmp.Equals(Key, key)) {
                added = false;
                return EqualityComparer<TValue>.Default.Equals(Value, value) ? this : new LeafNode(key, value);
            }
            added = true;
            var oldHash = (uint)cmp.GetHashCode(Key);
            if (oldHash == hash) return new CollisionNode(hash, new LeafNode(key, value), this);
            return MergeLeaves(shift, oldHash, this, hash, new LeafNode(key, value));
        }

        internal override Node? Remove(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, out bool removed) {
            if (cmp.Equals(Key, key)) { removed = true; return null; }
            removed = false; return this;
        }

        internal override IEnumerable<KeyValuePair<TKey, TValue>> Enumerate() { yield return new(Key, Value); }
    }

    internal sealed class BitmapNode : Node {
        internal readonly int Bitmap;
        internal readonly Node[] Children;
        internal BitmapNode(int bitmap, Node[] children) { Bitmap = bitmap; Children = children; }

        internal override bool TryGet(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, [MaybeNullWhen(false)] out TValue value) {
            var idx = (int)(hash >> shift) & Mask;
            var bit = 1 << idx;
            if ((Bitmap & bit) == 0) { value = default; return false; }
            var cIdx = BitOperations.PopCount((uint)(Bitmap & (bit - 1)));
            return Children[cIdx].TryGet(shift + Bits, hash, cmp, key, out value);
        }

        internal override Node Add(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, TValue value, out bool added) {
            var idx = (int)(hash >> shift) & Mask;
            var bit = 1 << idx;
            var cIdx = BitOperations.PopCount((uint)(Bitmap & (bit - 1)));
            if ((Bitmap & bit) != 0) {
                var newChild = Children[cIdx].Add(shift + Bits, hash, cmp, key, value, out added);
                if (ReferenceEquals(newChild, Children[cIdx])) return this;
                var newChildren = new Node[Children.Length];
                Array.Copy(Children, newChildren, Children.Length);
                newChildren[cIdx] = newChild;
                return new BitmapNode(Bitmap, newChildren);
            }
            added = true;
            var newBitmap = Bitmap | bit;
            var newChildren2 = new Node[Children.Length + 1];
            Array.Copy(Children, newChildren2, cIdx);
            newChildren2[cIdx] = new LeafNode(key, value);
            Array.Copy(Children, cIdx, newChildren2, cIdx + 1, Children.Length - cIdx);
            return newChildren2.Length >= MaxBitmapSize
                ? UpgradeToArrayNode(newBitmap, newChildren2)
                : new BitmapNode(newBitmap, newChildren2);
        }

        internal override Node? Remove(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, out bool removed) {
            var idx = (int)(hash >> shift) & Mask;
            var bit = 1 << idx;
            if ((Bitmap & bit) == 0) { removed = false; return this; }
            var cIdx = BitOperations.PopCount((uint)(Bitmap & (bit - 1)));
            var newChild = Children[cIdx].Remove(shift + Bits, hash, cmp, key, out removed);
            if (!removed) return this;
            if (newChild is not null) {
                if (Children.Length == 1 && newChild is LeafNode shrunkLeaf) return shrunkLeaf;
                var newChildren = new Node[Children.Length];
                Array.Copy(Children, newChildren, Children.Length);
                newChildren[cIdx] = newChild;
                return new BitmapNode(Bitmap, newChildren);
            }
            var newBitmap = Bitmap ^ bit;
            if (newBitmap == 0) return null;
            var newChildren2 = new Node[Children.Length - 1];
            Array.Copy(Children, newChildren2, cIdx);
            Array.Copy(Children, cIdx + 1, newChildren2, cIdx, Children.Length - cIdx - 1);
            return newChildren2.Length == 1 && newChildren2[0] is LeafNode leaf ? leaf : new BitmapNode(newBitmap, newChildren2);
        }

        internal override IEnumerable<KeyValuePair<TKey, TValue>> Enumerate() {
            for (var i = 0; i < Children.Length; i++)
                foreach (var kv in Children[i].Enumerate()) yield return kv;
        }

        private ArrayNode UpgradeToArrayNode(int newBitmap, Node[] children) {
            var array = new Node?[Width];
            for (var i = 0; i < Width; i++) {
                var bit = 1 << i;
                if ((newBitmap & bit) != 0)
                    array[i] = children[BitOperations.PopCount((uint)(newBitmap & (bit - 1)))];
            }
            return new ArrayNode(children.Length, array);
        }
    }

    internal sealed class ArrayNode : Node {
        internal readonly int Count;
        internal readonly Node?[] Children;
        internal ArrayNode(int count, Node?[] children) { Count = count; Children = children; }

        internal override bool TryGet(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, [MaybeNullWhen(false)] out TValue value) {
            var idx = (int)(hash >> shift) & Mask;
            var child = Children[idx];
            if (child is null) { value = default; return false; }
            return child.TryGet(shift + Bits, hash, cmp, key, out value);
        }

        internal override Node Add(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, TValue value, out bool added) {
            var idx = (int)(hash >> shift) & Mask;
            var child = Children[idx];
            if (child is null) {
                added = true;
                var newChildren = (Node?[])Children.Clone();
                newChildren[idx] = new LeafNode(key, value);
                return new ArrayNode(Count + 1, newChildren);
            }
            var newChild = child.Add(shift + Bits, hash, cmp, key, value, out added);
            if (ReferenceEquals(newChild, child)) return this;
            var newChildren2 = (Node?[])Children.Clone();
            newChildren2[idx] = newChild;
            return new ArrayNode(Count, newChildren2);
        }

        internal override Node? Remove(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, out bool removed) {
            var idx = (int)(hash >> shift) & Mask;
            var child = Children[idx];
            if (child is null) { removed = false; return this; }
            var newChild = child.Remove(shift + Bits, hash, cmp, key, out removed);
            if (!removed) return this;
            if (newChild is not null) {
                var newChildren = (Node?[])Children.Clone();
                newChildren[idx] = newChild;
                return new ArrayNode(Count, newChildren);
            }
            var newCount = Count - 1;
            if (newCount < MinArraySize) return DowngradeToBitmapNode(idx);
            var newChildren2 = (Node?[])Children.Clone();
            newChildren2[idx] = null;
            return new ArrayNode(newCount, newChildren2);
        }

        internal override IEnumerable<KeyValuePair<TKey, TValue>> Enumerate() {
            for (var i = 0; i < Width; i++) {
                var child = Children[i];
                if (child is not null)
                    foreach (var kv in child.Enumerate()) yield return kv;
            }
        }

        private BitmapNode DowngradeToBitmapNode(int nullIdx) {
            var count = Count - 1;
            var children = new Node[count];
            var bitmap = 0;
            var j = 0;
            for (var i = 0; i < Width; i++) {
                if (i == nullIdx) continue;
                var child = Children[i];
                if (child is not null) { bitmap |= 1 << i; children[j++] = child; }
            }
            return new BitmapNode(bitmap, children);
        }
    }

    internal sealed class CollisionNode : Node {
        internal readonly uint Hash;
        internal readonly LeafNode[] Entries;
        internal CollisionNode(uint hash, params LeafNode[] entries) { Hash = hash; Entries = entries; }

        internal override bool TryGet(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, [MaybeNullWhen(false)] out TValue value) {
            if (Hash != hash) { value = default; return false; }
            for (var i = 0; i < Entries.Length; i++)
                if (cmp.Equals(Entries[i].Key, key)) { value = Entries[i].Value; return true; }
            value = default; return false;
        }

        internal override Node Add(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, TValue value, out bool added) {
            if (Hash != hash) { added = true; return MergeLeaves(shift, Hash, this, hash, new LeafNode(key, value)); }
            for (var i = 0; i < Entries.Length; i++)
                if (cmp.Equals(Entries[i].Key, key)) {
                    added = false;
                    var newEntries = (LeafNode[])Entries.Clone();
                    newEntries[i] = new LeafNode(key, value);
                    return new CollisionNode(Hash, newEntries);
                }
            added = true;
            var newEntries2 = new LeafNode[Entries.Length + 1];
            Array.Copy(Entries, newEntries2, Entries.Length);
            newEntries2[Entries.Length] = new LeafNode(key, value);
            return new CollisionNode(Hash, newEntries2);
        }

        internal override Node? Remove(int shift, uint hash, IEqualityComparer<TKey> cmp, TKey key, out bool removed) {
            if (Hash != hash) { removed = false; return this; }
            for (var i = 0; i < Entries.Length; i++)
                if (cmp.Equals(Entries[i].Key, key)) {
                    removed = true;
                    if (Entries.Length == 2) return Entries[1 - i];
                    var newEntries = new LeafNode[Entries.Length - 1];
                    Array.Copy(Entries, newEntries, i);
                    Array.Copy(Entries, i + 1, newEntries, i, Entries.Length - i - 1);
                    return new CollisionNode(Hash, newEntries);
                }
            removed = false; return this;
        }

        internal override IEnumerable<KeyValuePair<TKey, TValue>> Enumerate() {
            for (var i = 0; i < Entries.Length; i++) yield return new(Entries[i].Key, Entries[i].Value);
        }
    }

    private static Node MergeLeaves(int shift, uint hash1, Node node1, uint hash2, Node node2) {
        var idx1 = (int)(hash1 >> shift) & Mask;
        var idx2 = (int)(hash2 >> shift) & Mask;
        if (idx1 == idx2) {
            var subNode = MergeLeaves(shift + Bits, hash1, node1, hash2, node2);
            return new BitmapNode(1 << idx1, new[] { subNode });
        }
        var bitmap = (1 << idx1) | (1 << idx2);
        var children = idx1 < idx2 ? new[] { node1, node2 } : new[] { node2, node1 };
        return new BitmapNode(bitmap, children);
    }

    /// <summary>创建空实例,使用指定比较器。</summary>
    public static ImmutableHamT<TKey, TValue> Create(IEqualityComparer<TKey>? keyComparer = null)
        => new(0, null, keyComparer ?? EqualityComparer<TKey>.Default);

    /// <summary>从键值对序列创建实例。</summary>
    public static ImmutableHamT<TKey, TValue> CreateRange(IEnumerable<KeyValuePair<TKey, TValue>> items, IEqualityComparer<TKey>? keyComparer = null) {
        var result = Create(keyComparer);
        foreach (var kv in items) result = result.Add(kv.Key, kv.Value);
        return result;
    }

    /// <summary>创建可变构建器。</summary>
    public static Builder CreateBuilder(IEqualityComparer<TKey>? keyComparer = null)
        => new(keyComparer ?? EqualityComparer<TKey>.Default);

    /// <summary>获取可变构建器副本。</summary>
    public Builder ToBuilder() {
        var b = new Builder(_keyComparer);
        foreach (var kv in this) b[kv.Key] = kv.Value;
        return b;
    }

    /// <summary>获取指定键的值,不存在返回默认值。</summary>
    public TValue GetValue(TKey key, TValue defaultValue)
        => TryGetValue(key, out var value) ? value : defaultValue;

    /// <summary>可变构建器 — 内部用 Dictionary,ToImmutable 时 O(N) 重建。</summary>
    public sealed class Builder : IDictionary<TKey, TValue> {
        private readonly Dictionary<TKey, TValue> _dict;
        internal Builder(IEqualityComparer<TKey> comparer) => _dict = new(comparer);

        /// <summary>构建不可变 HAMT。</summary>
        public ImmutableHamT<TKey, TValue> ToImmutable() {
            var result = Create(_dict.Comparer);
            foreach (var kv in _dict) result = result.SetItem(kv.Key, kv.Value);
            return result;
        }

        /// <summary>元素数量。</summary>
        public int Count => _dict.Count;
        /// <summary>是否只读。</summary>
        public bool IsReadOnly => false;
        /// <summary>所有键。</summary>
        public ICollection<TKey> Keys => _dict.Keys;
        /// <summary>所有值。</summary>
        public ICollection<TValue> Values => _dict.Values;

        /// <summary>获取或设置值。</summary>
        public TValue this[TKey key] { get => _dict[key]; set => _dict[key] = value; }

        /// <summary>获取指定键的值,不存在返回默认值。</summary>
        public TValue? GetValueOrDefault(TKey key) => _dict.TryGetValue(key, out var value) ? value : default;

        /// <summary>添加键值对。</summary>
        public void Add(TKey key, TValue value) => _dict.Add(key, value);
        /// <summary>添加键值对。</summary>
        void ICollection<KeyValuePair<TKey, TValue>>.Add(KeyValuePair<TKey, TValue> item) => _dict.Add(item.Key, item.Value);
        /// <summary>移除键。</summary>
        public bool Remove(TKey key) => _dict.Remove(key);
        /// <summary>移除键值对。</summary>
        bool ICollection<KeyValuePair<TKey, TValue>>.Remove(KeyValuePair<TKey, TValue> item) => _dict.Remove(item.Key);
        /// <summary>清空。</summary>
        public void Clear() => _dict.Clear();
        /// <summary>是否包含键。</summary>
        public bool ContainsKey(TKey key) => _dict.ContainsKey(key);
        /// <summary>尝试获取值。</summary>
        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) => _dict.TryGetValue(key, out value);
        /// <summary>是否包含键值对。</summary>
        bool ICollection<KeyValuePair<TKey, TValue>>.Contains(KeyValuePair<TKey, TValue> item) => _dict.ContainsKey(item.Key);
        /// <summary>复制到数组。</summary>
        void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) {
            foreach (var kv in _dict) array[arrayIndex++] = kv;
        }
        /// <summary>遍历。</summary>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => _dict.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

/// <summary>ImmutableHamT LINQ 扩展方法 — 对齐 ToImmutableDictionary API。</summary>
public static class ImmutableHamTExtensions {
    /// <summary>将键值对序列转换为 ImmutableHamT。</summary>
    public static ImmutableHamT<TKey, TValue> ToImmutableHamT<TKey, TValue>(this IEnumerable<KeyValuePair<TKey, TValue>> source) where TKey : notnull {
        var result = ImmutableHamT<TKey, TValue>.Empty;
        foreach (var kv in source) result = result.Add(kv.Key, kv.Value);
        return result;
    }

    /// <summary>将序列按键选择器转换为 ImmutableHamT(value 为元素本身)。</summary>
    public static ImmutableHamT<TKey, TSource> ToImmutableHamT<TSource, TKey>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector) where TKey : notnull {
        var result = ImmutableHamT<TKey, TSource>.Empty;
        foreach (var item in source) result = result.Add(keySelector(item), item);
        return result;
    }

    /// <summary>将序列按选择器转换为 ImmutableHamT。</summary>
    public static ImmutableHamT<TKey, TValue> ToImmutableHamT<TSource, TKey, TValue>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector) where TKey : notnull {
        var result = ImmutableHamT<TKey, TValue>.Empty;
        foreach (var item in source) result = result.Add(keySelector(item), valueSelector(item));
        return result;
    }

    /// <summary>将序列按选择器转换为 ImmutableHamT,指定比较器。</summary>
    public static ImmutableHamT<TKey, TValue> ToImmutableHamT<TSource, TKey, TValue>(this IEnumerable<TSource> source, Func<TSource, TKey> keySelector, Func<TSource, TValue> valueSelector, IEqualityComparer<TKey> comparer) where TKey : notnull {
        var result = ImmutableHamT<TKey, TValue>.Create(comparer);
        foreach (var item in source) result = result.Add(keySelector(item), valueSelector(item));
        return result;
    }
}
