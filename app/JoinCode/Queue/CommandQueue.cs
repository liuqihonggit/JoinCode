namespace JoinCode.Queue;

/// <summary>
/// 命令队列优先级 — 对齐 TS 原版 的 QueuePriority（now > next > later）。
/// </summary>
public enum QueuePriority
{
    /// <summary>最高优先级：权限确认响应等需立即处理。</summary>
    Now = 0,

    /// <summary>默认优先级：用户输入。</summary>
    Next = 1,

    /// <summary>最低优先级：任务通知（不饥饿用户输入）。</summary>
    Later = 2,
}

/// <summary>
/// 命令来源 — 标识入队方，用于路由和审计。
/// </summary>
public enum CommandOrigin
{
    /// <summary>用户直接输入。</summary>
    User,

    /// <summary>后台任务通知。</summary>
    TaskNotification,

    /// <summary>权限确认响应。</summary>
    PermissionResponse,
}

/// <summary>
/// 入队命令 — 内容 + 来源 + 优先级。
/// </summary>
public sealed record QueuedCommand(string Content, CommandOrigin Origin, QueuePriority Priority);

/// <summary>
/// 优先级命令队列 — 三级优先级（Now &gt; Next &gt; Later），同优先级 FIFO，线程安全。
/// 对齐 TS 原版 的 messageQueueManager.ts 设计。
/// </summary>
public sealed class CommandQueue
{
    private readonly ConcurrentQueue<QueuedCommand> _now = new();
    private readonly ConcurrentQueue<QueuedCommand> _next = new();
    private readonly ConcurrentQueue<QueuedCommand> _later = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private int _count;

    /// <summary>当前队列总长度（线程安全读取）。</summary>
    public int Count => Volatile.Read(ref _count);

    /// <summary>入队命令到对应优先级子队列。</summary>
    /// <param name="cmd">入队命令。</param>
    public void Enqueue(QueuedCommand cmd)
    {
        var queue = cmd.Priority switch
        {
            QueuePriority.Now => _now,
            QueuePriority.Next => _next,
            QueuePriority.Later => _later,
            _ => throw new ArgumentOutOfRangeException(nameof(cmd))
        };
        queue.Enqueue(cmd);
        Interlocked.Increment(ref _count);
        _signal.Release();
    }

    /// <summary>按优先级出队（Now &gt; Next &gt; Later），同优先级 FIFO。</summary>
    /// <returns>最高优先级命令；队列空返回 null。</returns>
    public QueuedCommand? Dequeue()
    {
        if (_now.TryDequeue(out var cmd)) { Interlocked.Decrement(ref _count); return cmd; }
        if (_next.TryDequeue(out cmd)) { Interlocked.Decrement(ref _count); return cmd; }
        if (_later.TryDequeue(out cmd)) { Interlocked.Decrement(ref _count); return cmd; }
        return null;
    }

    /// <summary>尝试出队。</summary>
    /// <param name="cmd">出队的命令。</param>
    /// <returns>非空返回 true；空返回 false。</returns>
    public bool TryDequeue(out QueuedCommand cmd)
    {
        var dequeued = Dequeue();
        cmd = dequeued!;
        return dequeued is not null;
    }

    /// <summary>
    /// 异步出队 — 空队列时异步等待（不阻塞线程），有项时按优先级出队。
    /// 对齐 TS 原版 的队列驱动消费模式，供 REPL 主循环 await 使用。
    /// </summary>
    /// <param name="cancellationToken">取消令牌，触发时抛 <see cref="OperationCanceledException"/>。</param>
    /// <returns>出队的命令。</returns>
    public async Task<QueuedCommand> DequeueAsync(CancellationToken cancellationToken = default)
    {
        while (true)
        {
            if (TryDequeue(out var cmd)) return cmd;
            await _signal.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>获取当前队列快照（用于驱动 UI 渲染）。</summary>
    /// <returns>三级队列的只读快照。</returns>
    public QueueSnapshot GetSnapshot()
    {
        var now = _now.ToArray();
        var next = _next.ToArray();
        var later = _later.ToArray();
        return new QueueSnapshot(now, next, later);
    }
}

/// <summary>
/// 队列快照 — 三级队列的只读视图，驱动"投递中"组件渲染。
/// </summary>
public sealed record QueueSnapshot(
    IReadOnlyList<QueuedCommand> Now,
    IReadOnlyList<QueuedCommand> Next,
    IReadOnlyList<QueuedCommand> Later)
{
    /// <summary>总待处理数。</summary>
    public int TotalCount => Now.Count + Next.Count + Later.Count;

    /// <summary>所有命令按优先级顺序排列。</summary>
    public IReadOnlyList<QueuedCommand> All => Now.Concat(Next).Concat(Later).ToList();
}
