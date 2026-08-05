namespace Structura.Collections;

/// <summary>
/// 无锁线程安全定长环形队列 — SeqLock 屏障保证快照一致性,性能比肩单线程
/// 塞满后自动覆盖最旧元素,O(1) 写入;ToArray 获取一致快照,不会读到"写一半"
/// </summary>
/// <typeparam name="T">元素类型</typeparam>
public sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;
    private volatile int _seq;

    /// <summary>
    /// 初始化定长环形队列
    /// </summary>
    /// <param name="capacity">队列容量,塞满后新元素覆盖最旧元素</param>
    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _buffer = new T[capacity];
    }

    /// <summary>
    /// 队列容量(最大元素数)
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// 当前元素数(volatile read)
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// 是否已满
    /// </summary>
    public bool IsFull => _count == _buffer.Length;

    /// <summary>
    /// 按逻辑索引访问元素 — 0=最旧,Count-1=最新(SeqLock 保证一致性)
    /// </summary>
    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            while (true)
            {
                var s1 = _seq;
                if ((s1 & 1) != 0)
                    continue;
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
                var value = _buffer[GetPhysicalIndex(index)];
                if (s1 == _seq)
                    return value;
            }
        }
    }

    /// <summary>
    /// 最旧元素
    /// </summary>
    public T Oldest => this[0];

    /// <summary>
    /// 最新元素
    /// </summary>
    public T Latest => this[Count - 1];

    /// <summary>
    /// 添加元素 — SeqLock 包裹,Interlocked.Increment 插入完整内存屏障
    /// </summary>
    public void Add(T item)
    {
        Interlocked.Increment(ref _seq);
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length)
            _count++;
        Interlocked.Increment(ref _seq);
    }

    /// <summary>
    /// 清空队列 — SeqLock 包裹
    /// </summary>
    public void Clear()
    {
        Interlocked.Increment(ref _seq);
        _head = 0;
        _count = 0;
        Array.Clear(_buffer);
        Interlocked.Increment(ref _seq);
    }

    /// <summary>
    /// 获取一致快照 — SeqLock 验证版本号,写入期间重试
    /// </summary>
    public T[] ToArray()
    {
        while (true)
        {
            var s1 = _seq;
            if ((s1 & 1) != 0)
                continue;
            var count = _count;
            var head = _head;
            var result = new T[count];
            var start = count == _buffer.Length ? head : 0;
            for (var i = 0; i < count; i++)
                result[i] = _buffer[(start + i) % _buffer.Length];
            if (s1 == _seq)
                return result;
        }
    }

    /// <summary>
    /// 获取从 start 开始的 count 个元素的切片(基于一致快照)
    /// </summary>
    public IEnumerable<T> Slice(int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var snapshot = ToArray();
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + count, snapshot.Length);
        return SliceCore(snapshot, start, count);
    }

    private static IEnumerable<T> SliceCore(T[] snapshot, int start, int count)
    {
        for (var i = start; i < start + count; i++)
            yield return snapshot[i];
    }

    /// <summary>
    /// 从最旧到最新枚举元素(基于一致快照)
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        var snapshot = ToArray();
        foreach (var item in snapshot)
            yield return item;
    }

    /// <summary>
    /// 将逻辑索引转换为物理数组索引
    /// </summary>
    private int GetPhysicalIndex(int logicalIndex)
    {
        var start = _count == _buffer.Length ? _head : 0;
        return (start + logicalIndex) % _buffer.Length;
    }
}
