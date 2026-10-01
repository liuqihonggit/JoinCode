namespace Structura.Collections;

/// <summary>
/// 跳表节点 — 各层 Next 指针数组。
/// </summary>
internal sealed class SkipListNode<TKey, TValue> {
    /// <summary>键。</summary>
    public readonly TKey Key;
    /// <summary>值。</summary>
    public TValue Value;
    /// <summary>各层下一个节点指针。</summary>
    public SkipListNode<TKey, TValue>?[] Next;

    /// <summary>构造跳表节点。</summary>
    public SkipListNode(TKey key, TValue value, int level) {
        Key = key;
        Value = value;
        Next = new SkipListNode<TKey, TValue>?[level + 1];
    }
}

/// <summary>
/// 跳表 — 概率平衡有序数据结构，O(log n) 查找/插入/删除。
/// <para>读无锁（volatile），写需外部同步或 CAS。支持范围查询。</para>
/// <para>用途：增量索引的索引表，块ID→页位置映射。</para>
/// </summary>
/// <typeparam name="TKey">键类型，需 IComparable。</typeparam>
/// <typeparam name="TValue">值类型。</typeparam>
public sealed class SkipList<TKey, TValue> where TKey : IComparable<TKey> {
    private const int MaxLevel = 32;
    private const double Probability = 0.5;

    private readonly SkipListNode<TKey, TValue> _head;
    private volatile int _level;
    private volatile int _count;

    /// <summary>构造空跳表。</summary>
    public SkipList() {
        _head = new SkipListNode<TKey, TValue>(default!, default!, MaxLevel);
        _level = 0;
        _count = 0;
    }

    /// <summary>构造跳表（预分配提示容量）。</summary>
    public SkipList(int capacity) : this() { }

    /// <summary>条目数量。</summary>
    public int Count => _count;

    /// <summary>
    /// 查找 key 对应的 value。
    /// </summary>
    /// <param name="key">键。</param>
    /// <param name="value">输出的值。</param>
    /// <returns>true 表示找到；false 表示未找到。</returns>
    public bool TryGetValue(TKey key, out TValue value) {
        var node = SearchPredecessor(key);
        var next = node.Next[0];
        if (next is { } n && n.Key.CompareTo(key) == 0) {
            value = n.Value;
            return true;
        }
        value = default!;
        return false;
    }

    /// <summary>
    /// 插入或更新 key→value。
    /// </summary>
    /// <param name="key">键。</param>
    /// <param name="value">值。</param>
    public void Insert(TKey key, TValue value) {
        var update = new SkipListNode<TKey, TValue>?[MaxLevel + 1];
        var node = _head;
        for (var i = _level; i >= 0; i--) {
            while (node.Next[i] is { } next && next.Key.CompareTo(key) < 0) {
                node = next;
            }
            update[i] = node;
        }

        var existing = node.Next[0];
        if (existing is { } e && e.Key.CompareTo(key) == 0) {
            e.Value = value;
            return;
        }

        var newLevel = RandomLevel();
        if (newLevel > _level) {
            for (var i = _level + 1; i <= newLevel; i++) {
                update[i] = _head;
            }
            _level = newLevel;
        }

        var newNode = new SkipListNode<TKey, TValue>(key, value, newLevel);
        for (var i = 0; i <= newLevel; i++) {
            var up = update[i];
            if (up is null) continue;
            newNode.Next[i] = up.Next[i];
            up.Next[i] = newNode;
        }
        _count++;
    }

    /// <summary>
    /// 删除 key。
    /// </summary>
    /// <param name="key">键。</param>
    /// <returns>true 表示删除成功；false 表示 key 不存在。</returns>
    public bool Delete(TKey key) {
        var update = new SkipListNode<TKey, TValue>?[MaxLevel + 1];
        var node = _head;
        for (var i = _level; i >= 0; i--) {
            while (node.Next[i] is { } next && next.Key.CompareTo(key) < 0) {
                node = next;
            }
            update[i] = node;
        }

        var target = node.Next[0];
        if (target is null || target.Key.CompareTo(key) != 0) return false;

        for (var i = 0; i <= _level; i++) {
            var up = update[i];
            if (up is null || up.Next[i] != target) break;
            up.Next[i] = target.Next[i];
        }

        while (_level > 0 && _head.Next[_level] is null) {
            _level--;
        }
        _count--;
        return true;
    }

    /// <summary>
    /// 范围查询 [startKey, endKey) — 有序遍历。
    /// </summary>
    /// <param name="startKey">起始键（包含）。</param>
    /// <param name="endKey">结束键（不包含）。</param>
    /// <returns>有序键值对序列。</returns>
    public IEnumerable<(TKey Key, TValue Value)> Range(TKey startKey, TKey endKey) {
        var node = SearchPredecessor(startKey);
        var current = node.Next[0];
        while (current is { } c) {
            if (c.Key.CompareTo(endKey) >= 0) yield break;
            yield return (c.Key, c.Value);
            current = c.Next[0];
        }
    }

    /// <summary>
    /// 从 startKey 开始的有序遍历 — 无上限。
    /// </summary>
    /// <param name="startKey">起始键（包含）。</param>
    /// <returns>有序键值对序列。</returns>
    public IEnumerable<(TKey Key, TValue Value)> RangeFrom(TKey startKey) {
        var node = SearchPredecessor(startKey);
        var current = node.Next[0];
        while (current is { } c) {
            yield return (c.Key, c.Value);
            current = c.Next[0];
        }
    }

    /// <summary>
    /// 前缀范围查询 — key 以 prefix 开头的所有条目。
    /// </summary>
    /// <param name="prefix">前缀键。</param>
    /// <returns>有序键值对序列。</returns>
    public IEnumerable<(TKey Key, TValue Value)> PrefixRange(TKey prefix) {
        var node = SearchPredecessor(prefix);
        var current = node.Next[0];
        while (current is { } c) {
            if (!c.Key.ToString()!.StartsWith(prefix.ToString()!)) yield break;
            yield return (c.Key, c.Value);
            current = c.Next[0];
        }
    }

    /// <summary>有序遍历所有键值对。</summary>
    public IEnumerable<(TKey Key, TValue Value)> Enumerate() {
        var current = _head.Next[0];
        while (current is { } c) {
            yield return (c.Key, c.Value);
            current = c.Next[0];
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private SkipListNode<TKey, TValue> SearchPredecessor(TKey key) {
        var node = _head;
        for (var i = _level; i >= 0; i--) {
            while (node.Next[i] is { } next && next.Key.CompareTo(key) < 0) {
                node = next;
            }
        }
        return node;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int RandomLevel() {
        var level = 0;
        while (Random.Shared.NextDouble() < Probability && level < MaxLevel) {
            level++;
        }
        return level;
    }
}
