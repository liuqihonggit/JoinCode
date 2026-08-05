namespace Structura.Collections;

/// <summary>
<<<<<<< HEAD
/// 无锁线程安全定长环形队列 — SeqLock 屏障保证快照一致性,性能比肩单线程
/// 塞满后自动覆盖最旧元素,O(1) 写入;ToArray 获取一致快照,不会读到"写一半"
=======
/// 定长环形队列 — 塞满后自动覆盖最旧元素,O(1) 写入与索引访问
/// 用于滑动窗口场景,替代 List&lt;T&gt;+RemoveRange 的 O(n) 内存拷贝
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)
/// </summary>
/// <typeparam name="T">元素类型</typeparam>
public sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;
<<<<<<< HEAD
    private volatile int _seq;
=======
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)

    /// <summary>
    /// 初始化定长环形队列
    /// </summary>
    /// <param name="capacity">队列容量,塞满后新元素覆盖最旧元素</param>
    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _buffer = new T[capacity];
<<<<<<< HEAD
=======
        _head = 0;
        _count = 0;
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)
    }

    /// <summary>
    /// 队列容量(最大元素数)
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
<<<<<<< HEAD
    /// 当前元素数(volatile read)
=======
    /// 当前元素数
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)
    /// </summary>
    public int Count => _count;

    /// <summary>
<<<<<<< HEAD
    /// 是否已满
=======
    /// 是否已满(后续 Add 将覆盖最旧元素)
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)
    /// </summary>
    public bool IsFull => _count == _buffer.Length;

    /// <summary>
<<<<<<< HEAD
    /// 按逻辑索引访问元素 — 0=最旧,Count-1=最新(SeqLock 保证一致性)
=======
    /// 按逻辑索引访问元素 — 0=最旧,Count-1=最新
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)
    /// </summary>
    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
<<<<<<< HEAD
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
=======
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
            return _buffer[GetPhysicalIndex(index)];
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)
        }
    }

    /// <summary>
<<<<<<< HEAD
    /// 最旧元素
=======
    /// 最旧元素(逻辑索引 0)
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)
    /// </summary>
    public T Oldest => this[0];

    /// <summary>
<<<<<<< HEAD
    /// 最新元素
    /// </summary>
    public T Latest => this[Count - 1];

    /// <summary>
    /// 添加元素 — SeqLock 包裹,Interlocked.Increment 插入完整内存屏障
    /// </summary>
    public void Add(T item)
    {
        Interlocked.Increment(ref _seq);
=======
    /// 最新元素(逻辑索引 Count-1)
    /// </summary>
    public T Latest => this[_count - 1];

    /// <summary>
    /// 添加元素 — 未满时追加到尾部,已满时覆盖最旧元素
    /// </summary>
    public void Add(T item)
    {
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length)
            _count++;
<<<<<<< HEAD
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
=======
    }

    /// <summary>
    /// 清空队列,重置状态
    /// </summary>
    public void Clear()
    {
        _head = 0;
        _count = 0;
        Array.Clear(_buffer);
    }

    /// <summary>
    /// 获取从 start 开始的 count 个元素的切片(从最旧到最新的逻辑顺序)
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)
    /// </summary>
    public IEnumerable<T> Slice(int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
<<<<<<< HEAD
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
=======
        ArgumentOutOfRangeException.ThrowIfGreaterThan(start + count, _count);
        return SliceCore(start, count);
    }

    private IEnumerable<T> SliceCore(int start, int count)
    {
        for (var i = start; i < start + count; i++)
            yield return this[i];
    }

    /// <summary>
    /// 从最旧到最新枚举元素
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        for (var i = 0; i < _count; i++)
            yield return this[i];
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)
    }

    /// <summary>
    /// 将逻辑索引转换为物理数组索引
    /// </summary>
    private int GetPhysicalIndex(int logicalIndex)
    {
<<<<<<< HEAD
        var start = _count == _buffer.Length ? _head : 0;
=======
        var start = IsFull ? _head : 0;
>>>>>>> 647955cbb (feat: 添加 RingBuffer 定长环形队列并替换三个检测器的 List+RemoveRange)
        return (start + logicalIndex) % _buffer.Length;
    }
}
