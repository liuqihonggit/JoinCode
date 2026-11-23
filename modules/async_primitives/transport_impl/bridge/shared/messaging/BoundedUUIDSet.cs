namespace JoinCode.Transport.Bridge;

/// <summary>
/// 有界UUID集合 - 基于FIFO环形缓冲区的去重集合
/// <para>无锁实现：状态（环形缓冲 + 去重集合）以不可变快照 + CAS 发布，读写均无阻塞。</para>
/// </summary>
public sealed class BoundedUUIDSet : IAsyncDisposable {
    private sealed record State(string[] Buffer, int Head, int Count, HashSet<string> Set);

    private readonly int _capacity;
    private State _state;
    private int _disposed;

    /// <summary>
    /// 创建有界UUID集合
    /// </summary>
    /// <param name="capacity">最大容量</param>
    public BoundedUUIDSet(int capacity) {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity), "[TRN012] 容量必须大于0");

        _capacity = capacity;
        _state = new State(new string[capacity], 0, 0, new HashSet<string>(capacity, StringComparer.Ordinal));
    }

    /// <summary>
    /// 当前元素数量
    /// </summary>
    public Task<int> GetCountAsync(CancellationToken ct = default) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Volatile.Read(ref _state).Count);
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
    public Task<bool> AddAsync(string uuid, CancellationToken ct = default) {
        if (string.IsNullOrEmpty(uuid))
            throw new ArgumentException("[TRN013] UUID不能为空", nameof(uuid));

        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(AddCore(uuid));
    }

    /// <summary>
    /// 异步检查UUID是否存在于集合中
    /// </summary>
    /// <param name="uuid">UUID字符串</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否存在</returns>
    public Task<bool> HasAsync(string uuid, CancellationToken ct = default) {
        if (string.IsNullOrEmpty(uuid))
            return Task.FromResult(false);

        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(Volatile.Read(ref _state).Set.Contains(uuid));
    }

    /// <summary>
    /// 同步检查 UUID 是否存在 — 用于 handleIngressMessage 等同步上下文
    /// </summary>
    public bool Contains(string uuid) {
        if (string.IsNullOrEmpty(uuid))
            return false;

        return Volatile.Read(ref _state).Set.Contains(uuid);
    }

    /// <summary>
    /// 同步添加 UUID — 用于 handleIngressMessage 等同步上下文
    /// </summary>
    public void Add(string uuid) {
        if (string.IsNullOrEmpty(uuid))
            return;

        if (Volatile.Read(ref _disposed) != 0)
            return;
        AddCore(uuid);
    }

    /// <summary>
    /// 异步清空集合
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public Task ClearAsync(CancellationToken ct = default) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        Interlocked.Exchange(
            ref _state,
            new State(new string[_capacity], 0, 0, new HashSet<string>(_capacity, StringComparer.Ordinal)));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 异步获取集合中的所有UUID（按添加顺序，从旧到新）
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>UUID列表</returns>
    public Task<IReadOnlyList<string>> ToListAsync(CancellationToken ct = default) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        var snapshot = Volatile.Read(ref _state);
        if (snapshot.Count == 0)
            return Task.FromResult<IReadOnlyList<string>>([]);

        var startIndex = snapshot.Count == _capacity ? snapshot.Head : 0;
        var result = Enumerable.Range(0, snapshot.Count)
            .Select(i => snapshot.Buffer[(startIndex + i) % _capacity])
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(result);
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// CAS 添加单个 UUID — 环形满时淘汰最旧项，返回是否为新项
    /// </summary>
    private bool AddCore(string uuid) {
        while (true) {
            var snapshot = Volatile.Read(ref _state);
            if (snapshot.Set.Contains(uuid))
                return false;

            var buffer = (string[])snapshot.Buffer.Clone();
            var set = new HashSet<string>(snapshot.Set, StringComparer.Ordinal);
            var head = snapshot.Head;
            var count = snapshot.Count;

            if (count == _capacity) {
                set.Remove(buffer[head]);
            } else {
                count++;
            }

            buffer[head] = uuid;
            set.Add(uuid);
            head = (head + 1) % _capacity;

            var next = new State(buffer, head, count, set);
            if (Interlocked.CompareExchange(ref _state, next, snapshot) == snapshot)
                return true;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
