namespace Structura.Collections;

/// <summary>
/// 定长环形队列 — 塞满后自动覆盖最旧元素,O(1) 写入与索引访问
/// 用于滑动窗口场景,替代 List&lt;T&gt;+RemoveRange 的 O(n) 内存拷贝
/// </summary>
/// <typeparam name="T">元素类型</typeparam>
public sealed class RingBuffer<T>
{
    private readonly T[] _buffer;
    private int _head;
    private int _count;

    /// <summary>
    /// 初始化定长环形队列
    /// </summary>
    /// <param name="capacity">队列容量,塞满后新元素覆盖最旧元素</param>
    public RingBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _buffer = new T[capacity];
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// 队列容量(最大元素数)
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// 当前元素数
    /// </summary>
    public int Count => _count;

    /// <summary>
    /// 是否已满(后续 Add 将覆盖最旧元素)
    /// </summary>
    public bool IsFull => _count == _buffer.Length;

    /// <summary>
    /// 按逻辑索引访问元素 — 0=最旧,Count-1=最新
    /// </summary>
    public T this[int index]
    {
        get
        {
            ArgumentOutOfRangeException.ThrowIfNegative(index);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
            return _buffer[GetPhysicalIndex(index)];
        }
    }

    /// <summary>
    /// 最旧元素(逻辑索引 0)
    /// </summary>
    public T Oldest => this[0];

    /// <summary>
    /// 最新元素(逻辑索引 Count-1)
    /// </summary>
    public T Latest => this[_count - 1];

    /// <summary>
    /// 添加元素 — 未满时追加到尾部,已满时覆盖最旧元素
    /// </summary>
    public void Add(T item)
    {
        _buffer[_head] = item;
        _head = (_head + 1) % _buffer.Length;
        if (_count < _buffer.Length)
            _count++;
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
    /// </summary>
    public IEnumerable<T> Slice(int start, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
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
    }

    /// <summary>
    /// 将逻辑索引转换为物理数组索引
    /// </summary>
    private int GetPhysicalIndex(int logicalIndex)
    {
        var start = IsFull ? _head : 0;
        return (start + logicalIndex) % _buffer.Length;
    }
}
