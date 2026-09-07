namespace Core.Utils;

/// <summary>
/// 持久化存储接口 — 横切关注点,可替换实现(文件/Redis/数据库)。
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
public interface IPersistentStore<TCommand>
{
    /// <summary>持久化命令 — 发送前调用,崩溃后可恢复</summary>
    ValueTask PersistAsync(string actorId, TCommand command, CancellationToken ct);

    /// <summary>加载未确认的命令 — 启动时重放,恢复未处理消息</summary>
    IAsyncEnumerable<TCommand> LoadPendingAsync(string actorId, CancellationToken ct);

    /// <summary>确认命令已处理 — 处理完成后调用,从存储移除</summary>
    ValueTask AckAsync(string actorId, TCommand command, CancellationToken ct);
}

/// <summary>
/// 内存持久化存储 — 测试用,不真正持久化(进程崩溃后丢失)。
/// </summary>
public sealed class InMemoryPersistentStore<TCommand> : IPersistentStore<TCommand>
{
    private readonly ConcurrentQueue<TCommand> _pending = new();

    public ValueTask PersistAsync(string actorId, TCommand command, CancellationToken ct)
    {
        _pending.Enqueue(command);
        return ValueTask.CompletedTask;
    }

    public async IAsyncEnumerable<TCommand> LoadPendingAsync(string actorId, [EnumeratorCancellation] CancellationToken ct)
    {
        while (_pending.TryDequeue(out var cmd))
        {
            yield return cmd;
            await Task.Yield();
        }
    }

    public ValueTask AckAsync(string actorId, TCommand command, CancellationToken ct) => ValueTask.CompletedTask;
}

/// <summary>
/// 持久化邮箱 — 装饰器模式,包装 ActorBase 的 SendAsync。
/// <para>发送前先持久化,崩溃时可从存储恢复未处理消息。</para>
/// <para>处理完成后确认(Ack),从存储移除。</para>
/// <para>不继承 ActorBase,是装饰器;不侵入 ActorBase,可任意组合。</para>
/// </summary>
/// <typeparam name="TCommand">命令类型</typeparam>
public sealed class PersistentMailbox<TCommand> : IAsyncDisposable
{
    private readonly ActorBase<TCommand> _actor;
    private readonly IPersistentStore<TCommand> _store;
    private readonly string _actorId;
    private int _disposed;
    private int _pendingCount;

    /// <summary>底层 Actor — 外部可直接访问(发消息用 PersistentSendAsync)</summary>
    public ActorBase<TCommand> Actor => _actor;

    /// <summary>当前持久化但未确认的命令数</summary>
    public int PendingCount => Volatile.Read(ref _pendingCount);

    /// <summary>
    /// 构造持久化邮箱。
    /// </summary>
    /// <param name="actor">底层 Actor</param>
    /// <param name="store">持久化存储</param>
    /// <param name="actorId">Actor 唯一标识(用于存储分区)</param>
    public PersistentMailbox(ActorBase<TCommand> actor, IPersistentStore<TCommand> store, string actorId)
    {
        _actor = actor;
        _store = store;
        _actorId = actorId;
    }

    /// <summary>
    /// 持久化发送 — 先持久化再入队,崩溃时可恢复。
    /// </summary>
    public async ValueTask PersistentSendAsync(TCommand cmd, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _store.PersistAsync(_actorId, cmd, ct).ConfigureAwait(false);
        Interlocked.Increment(ref _pendingCount);
        await _actor.SendAsync(cmd, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 确认命令已处理 — 从存储移除,减少 PendingCount。
    /// </summary>
    public async ValueTask AckAsync(TCommand cmd, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await _store.AckAsync(_actorId, cmd, ct).ConfigureAwait(false);
        Interlocked.Decrement(ref _pendingCount);
    }

    /// <summary>
    /// 崩溃恢复 — 启动时重放未处理消息。
    /// </summary>
    public async Task RecoverAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        await foreach (var cmd in _store.LoadPendingAsync(_actorId, ct).ConfigureAwait(false))
        {
            await _actor.SendAsync(cmd, ct).ConfigureAwait(false);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(PersistentMailbox<TCommand>));
    }

    /// <summary>释放 — 释放底层 Actor</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        await _actor.DisposeAsync().ConfigureAwait(false);
    }
}
