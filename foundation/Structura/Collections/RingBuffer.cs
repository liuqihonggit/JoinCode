namespace Structura.Collections;

/// <summary>
/// 无锁线程安全定长环形队列 — 每槽位独立 seq 标记保证快照一致性,支持多生产者并发写入
/// 塞满后自动覆盖最旧元素,O(1) 写入;ToArray 获取一致快照,不会读到"写一半"
/// 物理缓冲向上取整到 2 次幂,索引用 &amp;(len-1) 位移替代取模(快路径);
/// 对外 Capacity 返回请求的逻辑容量,不随取整变化
/// </summary>
/// <typeparam name="T">元素类型</typeparam>
public sealed class RingBuffer<T>
{
    private readonly Slot[] _slots;
    private readonly int _capacity;
    private readonly long _mask;
    private long _tail;

    private struct Slot
    {
        internal long Seq;
        internal T Value;
    }

    /// <summary>
    /// 初始化定长环形队列
    /// </summary>
    /// <param name="capacity">队列容量,塞满后新元素覆盖最旧元素;物理缓冲内部取整到 2 次幂</param>
    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        var len = NextPowerOfTwo(capacity);
        _mask = len - 1;
        _slots = new Slot[len];
        for (var i = 0; i < len; i++)
            _slots[i].Seq = i;
    }

    /// <summary>
    /// 队列容量(最大元素数,请求值)
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// 当前元素数(volatile read,封顶到容量) — _tail 是单调递增的总写入计数,
    /// 并发 Add 下用 CAS 原子递增获取唯一写入槽位,读数取 Min 得上限
    /// </summary>
    public int Count => (int)Math.Min(Volatile.Read(ref _tail), _capacity);

    /// <summary>
    /// 是否已满
    /// </summary>
    public bool IsFull => Volatile.Read(ref _tail) >= _capacity;

    /// <summary>
    /// 按逻辑索引访问元素 — 0=最旧,Count-1=最新;每槽位双 seq 验证保证读到一致值
    /// </summary>
    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            while (true)
            {
                var tail = Volatile.Read(ref _tail);
                var count = Math.Min(tail, _capacity);
                ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, (int)count);
                var pos = tail - count + index;
                ref var slot = ref _slots[(int)(pos & _mask)];
                var seq1 = Volatile.Read(ref slot.Seq);
                if (seq1 != pos + 1)
                    continue;
                var value = slot.Value;
                if (seq1 == Volatile.Read(ref slot.Seq))
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
    /// 添加元素 — 多生产者通过 CAS do-while 循环原子抢占尾指针 _tail,获取唯一写入槽位 pos;
    /// 成功 CAS 后写入 _slots[pos &amp; _mask].Value,再 release-write 槽位 Seq=pos+1 标记可见;
    /// release 屏障保证 Value 先于 Seq 对读者可见,读者通过双 seq 验证检测"写一半"或被覆盖
    /// </summary>
    public void Add(T item)
    {
        long pos;
        long next;
        do
        {
            pos = Volatile.Read(ref _tail);
            next = pos + 1;
        } while (Interlocked.CompareExchange(ref _tail, next, pos) != pos);
        ref var slot = ref _slots[(int)(pos & _mask)];
        slot.Value = item;
        Volatile.Write(ref slot.Seq, pos + 1);
    }

    /// <summary>
    /// 清空队列 — 重置 _tail 和所有槽位 Seq 到初始状态;不应与 Add 并发调用
    /// </summary>
    public void Clear()
    {
        Interlocked.Exchange(ref _tail, 0);
        for (var i = 0; i < _slots.Length; i++)
            _slots[i].Seq = i;
    }

    /// <summary>
    /// 获取一致快照 — 每槽位双 seq 验证(seq1→读Value→seq2,seq1==seq2 则未被覆盖);
    /// 任一槽位正在被写或被覆盖则整体重试
    /// </summary>
    public T[] ToArray()
    {
        while (true)
        {
            var tail = Volatile.Read(ref _tail);
            var count = Math.Min(tail, _capacity);
            if (count == 0)
                return [];
            var start = tail - count;
            var result = new T[(int)count];
            var ok = true;
            for (var i = 0; i < count; i++)
            {
                var pos = start + i;
                ref var slot = ref _slots[(int)(pos & _mask)];
                var seq1 = Volatile.Read(ref slot.Seq);
                if (seq1 != pos + 1)
                {
                    ok = false;
                    break;
                }
                result[i] = slot.Value;
                if (seq1 != Volatile.Read(ref slot.Seq))
                {
                    ok = false;
                    break;
                }
            }
            if (ok)
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
