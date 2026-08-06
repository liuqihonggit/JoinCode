namespace Structura.Collections;

/// <summary>
/// 多生产者单消费者无锁有界环形队列 — 生产者用 CAS do-while 抢占尾指针,消费者单线程移动头指针
/// 物理缓冲向上取整到 2 次幂,索引用 &amp;(len-1) 位移替代取模;留一空位区分空/满
/// 生产者尾指针与消费者头指针各占独立缓存行(PaddedInt 填充),避免伪共享
/// 兼容覆盖式快照语义:Add 满则丢弃最旧再入队,ToArray/indexer/Slice 只读不消费
/// </summary>
/// <typeparam name="T">队列元素类型</typeparam>
public sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private readonly int _capacity;
    private readonly int _mask;
    private PaddedInt _producerTail;
    private PaddedInt _consumerHead;
    private int _cachedProducerTail;

#pragma warning disable 0169
    private struct PaddedInt
    {
        internal int Value;
        private long _p1, _p2, _p3, _p4, _p5, _p6, _p7;
    }
#pragma warning restore 0169

    /// <summary>
    /// 初始化队列
    /// </summary>
    /// <param name="capacity">期望容量;物理缓冲向上取整到 2 次幂,实际可用容量 = 物理大小 - 1(留一空位区分空/满)</param>
    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        var actualSize = NextPowerOfTwo(capacity + 1);
        _buffer = new T[actualSize];
        _capacity = actualSize;
        _mask = actualSize - 1;
        _producerTail = default;
        _consumerHead = default;
        _cachedProducerTail = 0;
    }

    /// <summary>
    /// 队列容量(最大元素数,物理大小 - 1)
    /// </summary>
    public int Capacity => _capacity - 1;

    /// <summary>
    /// 队列中元素数量(近似值,并发下不保证精确)
    /// </summary>
    public int Count
    {
        get
        {
            var head = Volatile.Read(ref _consumerHead.Value);
            var tail = Volatile.Read(ref _producerTail.Value);
            return (tail - head + _capacity) & _mask;
        }
    }

    /// <summary>
    /// 是否为空
    /// </summary>
    public bool IsEmpty => Volatile.Read(ref _consumerHead.Value) == Volatile.Read(ref _producerTail.Value);

    /// <summary>
    /// 是否已满
    /// </summary>
    public bool IsFull
    {
        get
        {
            var head = Volatile.Read(ref _consumerHead.Value);
            var tail = Volatile.Read(ref _producerTail.Value);
            return ((tail + 1) & _mask) == head;
        }
    }

    /// <summary>
    /// 尝试入队(多生产者安全) — CAS do-while 循环抢占尾指针,满则返回 false
    /// </summary>
    /// <param name="item">要入队的元素</param>
    /// <returns>成功入队返回 true;队列已满返回 false</returns>
    /// <exception cref="ArgumentNullException">item 为 null</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnqueue(T item)
    {
        ArgumentNullException.ThrowIfNull(item);

        int currentTail;
        int nextTail;
        do
        {
            currentTail = Volatile.Read(ref _producerTail.Value);
            nextTail = (currentTail + 1) & _mask;

            if (nextTail == Volatile.Read(ref _consumerHead.Value))
            {
                _cachedProducerTail = currentTail;
                if (nextTail == Volatile.Read(ref _consumerHead.Value))
                    return false;
            }
        }
        while (Interlocked.CompareExchange(ref _producerTail.Value, nextTail, currentTail) != currentTail);

        _buffer[currentTail] = item;
        Interlocked.MemoryBarrier();
        return true;
    }

    /// <summary>
    /// 尝试出队(单消费者安全) — 无 CAS,仅 volatile 读写移动头指针
    /// </summary>
    /// <param name="item">出队的元素</param>
    /// <returns>成功出队返回 true;队列为空返回 false</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryDequeue(out T item)
    {
        item = default!;
        var currentHead = Volatile.Read(ref _consumerHead.Value);
        var currentTail = Volatile.Read(ref _producerTail.Value);

        if (currentHead == currentTail)
        {
            _cachedProducerTail = currentTail;
            return false;
        }

        item = _buffer[currentHead];
        Interlocked.MemoryBarrier();
        Volatile.Write(ref _consumerHead.Value, (currentHead + 1) & _mask);
        return true;
    }

    /// <summary>
    /// 批量入队 — 逐个 TryEnqueue,遇满提前终止
    /// </summary>
    /// <param name="items">源数组</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="count">期望入队数</param>
    /// <returns>实际入队数</returns>
    public int EnqueueBatch(T[] items, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(items);
        if (offset < 0 || count < 0 || offset + count > items.Length)
            throw new ArgumentOutOfRangeException();

        var enqueued = 0;
        for (var i = 0; i < count; i++)
        {
            if (TryEnqueue(items[offset + i]))
                enqueued++;
            else
                break;
        }
        return enqueued;
    }

    /// <summary>
    /// 批量出队 — 逐个 TryDequeue,遇空提前终止
    /// </summary>
    /// <param name="output">目标数组</param>
    /// <param name="offset">起始偏移</param>
    /// <param name="count">期望出队数</param>
    /// <returns>实际出队数</returns>
    public int DequeueBatch(T[] output, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (offset < 0 || count < 0 || offset + count > output.Length)
            throw new ArgumentOutOfRangeException();

        var dequeued = 0;
        for (var i = 0; i < count; i++)
        {
            if (TryDequeue(out var item))
            {
                output[offset + i] = item;
                dequeued++;
            }
            else
                break;
        }
        return dequeued;
    }

    /// <summary>
    /// 添加元素(覆盖式) — TryEnqueue 满则 TryDequeue 丢弃最旧再入队,保证不丢新元素
    /// </summary>
    /// <param name="item">要添加的元素</param>
    public void Add(T item)
    {
        while (!TryEnqueue(item))
            TryDequeue(out _);
    }

    /// <summary>
    /// 获取一致只读快照(不消费元素) — 读 head/tail 遍历 _buffer 拷贝,不移动指针
    /// </summary>
    public T[] ToArray()
    {
        var head = Volatile.Read(ref _consumerHead.Value);
        var tail = Volatile.Read(ref _producerTail.Value);
        var count = (tail - head + _capacity) & _mask;
        var result = new T[count];
        for (var i = 0; i < count; i++)
            result[i] = _buffer[(head + i) & _mask];
        return result;
    }

    /// <summary>
    /// 按逻辑索引访问元素(只读,不消费) — 0=最旧,Count-1=最新
    /// </summary>
    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            var head = Volatile.Read(ref _consumerHead.Value);
            var tail = Volatile.Read(ref _producerTail.Value);
            var count = (tail - head + _capacity) & _mask;
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
            return _buffer[(head + index) & _mask];
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
    /// 获取从 start 开始的 count 个元素的切片(只读快照)
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
    /// 从最旧到最新枚举元素(只读快照)
    /// </summary>
    public IEnumerator<T> GetEnumerator()
    {
        var snapshot = ToArray();
        foreach (var item in snapshot)
            yield return item;
    }

    /// <summary>
    /// 清空队列
    /// </summary>
    public void Clear()
    {
        Volatile.Write(ref _consumerHead.Value, Volatile.Read(ref _producerTail.Value));
    }

    /// <summary>
    /// 向上取整到不小于 value 的最小 2 次幂
    /// </summary>
    private static int NextPowerOfTwo(int value)
    {
        var v = value - 1;
        v |= v >> 1;
        v |= v >> 2;
        v |= v >> 4;
        v |= v >> 8;
        v |= v >> 16;
        return v + 1;
    }
}
