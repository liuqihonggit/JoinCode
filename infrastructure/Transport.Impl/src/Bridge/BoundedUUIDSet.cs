namespace JoinCode.Transport.Bridge;

/// <summary>
/// 有界UUID集合 - 基于FIFO环形缓冲区的去重集合
/// 用于消息去重，防止重复处理相同的消息
/// </summary>
public sealed class BoundedUUIDSet : IAsyncDisposable
{
    private readonly string[] _buffer;
    private readonly int _capacity;
    private readonly HashSet<string> _set;
    private readonly SemaphoreSlim _lock;

    private int _head;
    private int _count;

    /// <summary>
    /// 创建有界UUID集合
    /// </summary>
    /// <param name="capacity">最大容量</param>
    public BoundedUUIDSet(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "容量必须大于0");

        _capacity = capacity;
        _buffer = new string[capacity];
        _set = new HashSet<string>(capacity, StringComparer.Ordinal);
        _lock = new SemaphoreSlim(1, 1);
        _head = 0;
        _count = 0;
    }

    /// <summary>
    /// 当前元素数量
    /// </summary>
    public async Task<int> GetCountAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _count;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 最大容量
    /// </summary>
    public int Capacity => _capacity;

    /// <summary>
    /// 异步添加UUID到集合
    /// 如果已存在返回false，否则添加并返回true
    /// </summary>
    /// <param name="uuid">UUID字符串</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功添加（false表示已存在）</returns>
    public async Task<bool> AddAsync(string uuid, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(uuid))
            throw new ArgumentException("UUID不能为空", nameof(uuid));

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_set.Contains(uuid))
                return false;

            // 如果已满，移除最旧的元素
            if (_count == _capacity)
            {
                var oldest = _buffer[_head];
                _set.Remove(oldest);
            }
            else
            {
                _count++;
            }

            // 添加新元素
            _buffer[_head] = uuid;
            _set.Add(uuid);

            // 移动头指针
            _head = (_head + 1) % _capacity;

            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 异步检查UUID是否存在于集合中
    /// </summary>
    /// <param name="uuid">UUID字符串</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否存在</returns>
    public async Task<bool> HasAsync(string uuid, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(uuid))
            return false;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _set.Contains(uuid);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 同步检查 UUID 是否存在 — 用于 handleIngressMessage 等同步上下文
    /// </summary>
    public bool Contains(string uuid)
    {
        if (string.IsNullOrEmpty(uuid))
            return false;

        if (!_lock.Wait(0))
            return false; // 无法获取锁，保守返回 false

        try
        {
            return _set.Contains(uuid);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 同步添加 UUID — 用于 handleIngressMessage 等同步上下文
    /// </summary>
    public void Add(string uuid)
    {
        if (string.IsNullOrEmpty(uuid))
            return;

        if (!_lock.Wait(0))
            return; // 无法获取锁，保守跳过

        try
        {
            if (_set.Contains(uuid))
                return;

            // 如果已满，移除最旧的元素
            if (_count == _capacity)
            {
                var oldest = _buffer[_head];
                _set.Remove(oldest);
            }
            else
            {
                _count++;
            }

            _buffer[_head] = uuid;
            _set.Add(uuid);
            _head = (_head + 1) % _capacity;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 异步清空集合
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task ClearAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            Array.Clear(_buffer, 0, _buffer.Length);
            _set.Clear();
            _head = 0;
            _count = 0;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// 异步获取集合中的所有UUID（按添加顺序，从旧到新）
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>UUID列表</returns>
    public async Task<IReadOnlyList<string>> ToListAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_count == 0)
                return new List<string>();

            // 计算最旧元素的索引
            var startIndex = _count == _capacity ? _head : 0;

            return Enumerable.Range(0, _count)
                .Select(i => _buffer[(startIndex + i) % _capacity])
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _lock.Dispose();
    }
}
