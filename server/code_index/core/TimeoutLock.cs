namespace JoinCode.CodeIndex.Threading;

/// <summary>
/// 带超时的异步锁包装 — 包装 AsyncLock 提供超时获取与死锁检测
/// </summary>
internal sealed class TimeoutLock : IDisposable {
    private readonly AsyncLock _semaphore = new();
    private readonly TimeSpan _defaultTimeout;
    private readonly string _lockName;
    private readonly Action<string>? _log;
    private int _disposed;

    /// <summary>
    /// 构造超时锁
    /// </summary>
    /// <param name="lockName">锁名（用于日志与异常消息）</param>
    /// <param name="defaultTimeout">默认超时（未指定时使用 5 秒）</param>
    /// <param name="log">日志回调</param>
    public TimeoutLock(string lockName, TimeSpan? defaultTimeout = null, Action<string>? log = null) {
        ArgumentNullException.ThrowIfNull(lockName);

        _lockName = lockName;
        _defaultTimeout = defaultTimeout ?? TimeSpan.FromSeconds(5);

        _log = log;
    }

    /// <summary>
    /// 异步获取锁 — 超时则抛出 TimeoutException 并标记可能死锁
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <param name="timeout">本次获取超时（未指定使用默认超时）</param>
    /// <returns>释放器，释放时归还锁</returns>
    public async Task<IDisposable> AcquireAsync(CancellationToken ct, TimeSpan? timeout = null) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var actualTimeout = timeout ?? _defaultTimeout;
        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquiring (timeout={actualTimeout.TotalSeconds}s)...");

        var guard = _semaphore.TryLock();
        if (guard is null) {
            var msg = $"[TimeoutLock:{_lockName}] TIMEOUT: failed to acquire within {actualTimeout.TotalSeconds}s. Possible deadlock detected.";
            _log?.Invoke(msg);
            System.Diagnostics.Trace.TraceError(msg);
            throw new TimeoutException($"Lock '{_lockName}' could not be acquired within {actualTimeout.TotalSeconds}s. Possible deadlock detected.");
        }

        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquired");
        return new Releaser(_lockName, guard, _log);
    }

    /// <summary>
    /// 同步获取锁 — 超时则抛出 TimeoutException 并标记可能死锁
    /// </summary>
    /// <param name="timeout">本次获取超时（未指定使用默认超时）</param>
    /// <returns>释放器，释放时归还锁</returns>
    public IDisposable Acquire(TimeSpan? timeout = null) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var actualTimeout = timeout ?? _defaultTimeout;
        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquiring sync (timeout={actualTimeout.TotalSeconds}s)...");

        var guard = _semaphore.TryLock();
        if (guard is null) {
            var msg = $"[TimeoutLock:{_lockName}] TIMEOUT: failed to acquire within {actualTimeout.TotalSeconds}s. Possible deadlock detected.";
            _log?.Invoke(msg);
            System.Diagnostics.Trace.TraceError(msg);
            throw new TimeoutException($"Lock '{_lockName}' could not be acquired within {actualTimeout.TotalSeconds}s. Possible deadlock detected.");
        }

        _log?.Invoke($"[TimeoutLock:{_lockName}] Acquired sync");
        return new Releaser(_lockName, guard, _log);
    }

    /// <summary>
    /// 释放锁资源
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _semaphore.Dispose();
    }

    /// <summary>
    /// 锁释放器 — Dispose 时归还底层锁并记录日志
    /// </summary>
    private sealed class Releaser : IDisposable {
        private readonly IDisposable _guard;
        private readonly string _name;
        private readonly Action<string>? _log;
        private int _disposed;

        /// <summary>
        /// 构造释放器
        /// </summary>
        /// <param name="name">锁名（用于日志）</param>
        /// <param name="guard">底层锁守卫</param>
        /// <param name="log">日志回调</param>
        public Releaser(string name, IDisposable guard, Action<string>? log) {
            _name = name;
            _guard = guard;
            _log = log;
        }

        /// <summary>
        /// 释放底层锁并记录日志
        /// </summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _guard.Dispose();
            _log?.Invoke($"[TimeoutLock:{_name}] Released");
        }
    }
}