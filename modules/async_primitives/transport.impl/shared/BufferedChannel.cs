namespace JoinCode.Transport;

/// <summary>
/// 缓冲通道 — 无锁字符串行缓冲器，支持全量读取、增量读取和谓词检查。
/// <para>状态以不可变快照 + CAS 发布：写入/消费/清空各自构造新快照后原子替换，读取直接取当前快照。</para>
/// </summary>
public sealed class BufferedChannel : IDisposable {
    private sealed record Snapshot(string[] Lines, int ConsumedIndex);

    private Snapshot _state = new([], 0);
    private int _disposed;

    /// <summary>
    /// 添加一行到缓冲区
    /// </summary>
    /// <param name="line">要添加的行</param>
    /// <param name="ct">取消令牌</param>
    public Task AddAsync(string line, CancellationToken ct = default) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        while (true) {
            var snapshot = Volatile.Read(ref _state);
            var next = snapshot with { Lines = [.. snapshot.Lines, line] };
            if (Interlocked.CompareExchange(ref _state, next, snapshot) == snapshot)
                return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 获取缓冲区全部内容（用换行符连接）
    /// </summary>
    /// <param name="lockTimeout">保留参数（无锁实现立即完成，不再有锁等待）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>用换行符连接的全部缓冲内容</returns>
    public Task<string> GetAllAsync(TimeSpan lockTimeout, CancellationToken ct = default) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(string.Join("\n", Volatile.Read(ref _state).Lines));
    }

    /// <summary>
    /// 获取自上次调用以来的增量内容（用换行符连接）
    /// </summary>
    /// <param name="lockTimeout">保留参数（无锁实现立即完成，不再有锁等待）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>增量内容；无新内容时返回空字符串</returns>
    public Task<string> GetIncrementalAsync(TimeSpan lockTimeout, CancellationToken ct = default) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        while (true) {
            var snapshot = Volatile.Read(ref _state);
            if (snapshot.ConsumedIndex >= snapshot.Lines.Length)
                return Task.FromResult(string.Empty);

            var result = string.Join("\n", snapshot.Lines[snapshot.ConsumedIndex..]);
            var next = snapshot with { ConsumedIndex = snapshot.Lines.Length };
            if (Interlocked.CompareExchange(ref _state, next, snapshot) == snapshot)
                return Task.FromResult(result);
        }
    }

    /// <summary>
    /// 清空缓冲区并重置消费索引
    /// </summary>
    /// <param name="lockTimeout">保留参数（无锁实现立即完成，不再有锁等待）</param>
    /// <param name="ct">取消令牌</param>
    public Task ClearAsync(TimeSpan lockTimeout, CancellationToken ct = default) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        Interlocked.Exchange(ref _state, new Snapshot([], 0));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 对缓冲区全部内容（用换行符连接）应用谓词判断
    /// </summary>
    /// <param name="predicate">谓词函数</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>谓词返回值</returns>
    public Task<bool> TryPredicateAsync(Func<string, bool> predicate, CancellationToken ct = default) {
        ThrowIfDisposed();
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(predicate(string.Join("\n", Volatile.Read(ref _state).Lines)));
    }

    /// <summary>释放实例（幂等）— 无锁实现无内部资源。</summary>
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
}
