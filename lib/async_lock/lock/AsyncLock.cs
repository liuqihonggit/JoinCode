namespace Core.Utils;

/// <summary>
/// 锁/并发限流原语 — SemaphoreSlim 的薄封装,支持互斥 (1,1) 和并发限流 (N,N) 两种语义。
/// 提供同步 <c>TryLock</c> 和异步 <c>TryLockAsync</c>。
/// async 方法中用 <c>TryLockAsync</c> 避免线程池饥饿;非 async 上下文用 <c>TryLock</c>。
/// 默认5s超时(可经 <see cref="AsyncLock(string, TimeSpan)"/> 构造按实例配置),超时返回 null 并记录日志,取消抛 OperationCanceledException。
/// 内部接入 <see cref="LockRegistry"/> 诊断:获取/释放时记录调用栈、线程、时间,卡死时调用 <see cref="LockRegistry.DumpAll"/> 精确定位。
/// </summary>
public sealed class AsyncLock : IDisposable {
    /// <summary>
    /// 默认锁等待超时 — <see cref="TryLock(CancellationToken)"/> 在 ct 无超时时使用此值。
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 惰性注册当前 async 逻辑流的 FlowId — 首次获取锁时若未注册则自动分配。
    /// AsyncLocal 跨 await 自动流转,同一 async 流后续获取锁复用同一 FlowId,死锁检测正确构建 wait-for graph。
    /// </summary>
    private static void EnsureFlowRegistered() => LockRegistry.EnsureFlowRegistered();

    private readonly SemaphoreSlim _semaphore;
    private readonly string _name;
    private readonly TimeSpan _timeout;
    private readonly LockTimeoutPolicy _timeoutPolicy;
    private readonly int _registryId;
    private int _disposed;

    /// <summary>
    /// 锁名称 — 诊断输出中显示此名称,便于定位"哪个环节"的锁。
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// 构造匿名锁。锁名自动生成 <c>AsyncLock#{n}</c>,诊断时用调用栈定位。超时用 <see cref="DefaultTimeout"/>。
    /// </summary>
    public AsyncLock() {
        _semaphore = new SemaphoreSlim(1, 1);
        _name = $"AsyncLock#{LockRegistry.Count + 1}";
        _timeout = DefaultTimeout;
        _timeoutPolicy = LockTimeoutPolicy.Throw;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名锁 — 诊断输出中显示此名称,便于定位"哪个环节"的锁。超时用 <see cref="DefaultTimeout"/>。
    /// </summary>
    public AsyncLock(string name) {
        _semaphore = new SemaphoreSlim(1, 1);
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = DefaultTimeout;
        _timeoutPolicy = LockTimeoutPolicy.Throw;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名锁并指定超时策略 — 生产环境用 <see cref="LockTimeoutPolicy.Crash"/>,测试用 <see cref="LockTimeoutPolicy.Throw"/>。
    /// </summary>
    public AsyncLock(string name, LockTimeoutPolicy timeoutPolicy) {
        _semaphore = new SemaphoreSlim(1, 1);
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = DefaultTimeout;
        _timeoutPolicy = timeoutPolicy;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名锁并指定超时 — 不同锁可独立配置超时(IO 密集锁可设 30s,内存状态锁 5s)。
    /// </summary>
    public AsyncLock(string name, TimeSpan timeout) {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "超时必须为正值。");
        _semaphore = new SemaphoreSlim(1, 1);
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = timeout;
        _timeoutPolicy = LockTimeoutPolicy.Throw;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名锁并指定超时+超时策略 — IO 密集锁可设 30s+Crash,内存状态锁 5s+Crash。
    /// </summary>
    public AsyncLock(string name, TimeSpan timeout, LockTimeoutPolicy timeoutPolicy) {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "超时必须为正值。");
        _semaphore = new SemaphoreSlim(1, 1);
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = timeout;
        _timeoutPolicy = timeoutPolicy;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造并发限流锁 — (1,1) 为互斥,(N,N) 为并发限流。签名与 <see cref="SemaphoreSlim"/> 一致,降低迁移成本。
    /// </summary>
    public AsyncLock(int initialCount, int maxCount) {
        if (initialCount < 0 || maxCount < 1 || initialCount > maxCount)
            throw new ArgumentOutOfRangeException(
                nameof(initialCount),
                "initialCount 必须 >= 0,maxCount 必须 >= 1,且 initialCount <= maxCount。");
        _semaphore = new SemaphoreSlim(initialCount, maxCount);
        _name = $"AsyncLock#{LockRegistry.Count + 1}";
        _timeout = DefaultTimeout;
        _timeoutPolicy = LockTimeoutPolicy.Throw;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名并发限流锁 — (1,1) 为互斥,(N,N) 为并发限流。签名与 <see cref="SemaphoreSlim"/> 一致,同时支持具名诊断。
    /// </summary>
    public AsyncLock(string name, int initialCount, int maxCount) {
        if (initialCount < 0 || maxCount < 1 || initialCount > maxCount)
            throw new ArgumentOutOfRangeException(
                nameof(initialCount),
                "initialCount 必须 >= 0,maxCount 必须 >= 1,且 initialCount <= maxCount。");
        _semaphore = new SemaphoreSlim(initialCount, maxCount);
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = DefaultTimeout;
        _timeoutPolicy = LockTimeoutPolicy.Throw;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名并发限流锁并指定超时 — (1,1) 为互斥,(N,N) 为并发限流。
    /// </summary>
    public AsyncLock(string name, int initialCount, int maxCount, TimeSpan timeout) {
        if (initialCount < 0 || maxCount < 1 || initialCount > maxCount)
            throw new ArgumentOutOfRangeException(
                nameof(initialCount),
                "initialCount 必须 >= 0,maxCount 必须 >= 1,且 initialCount <= maxCount。");
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "超时必须为正值。");
        _semaphore = new SemaphoreSlim(initialCount, maxCount);
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = timeout;
        _timeoutPolicy = LockTimeoutPolicy.Throw;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 尝试同步获取锁。成功返回 Releaser,超时返回 null 并通过 <see cref="LockRegistry"/> 记录日志,取消抛 <see cref="OperationCanceledException"/>。
    /// <para>超时规则:若 <paramref name="ct"/> 可被取消(调用方已绑定超时),使用调用方的 ct;否则使用 <see cref="DefaultTimeout"/>(5s)。</para>
    /// </summary>
    public IDisposable? TryLock(CancellationToken ct = default) {
        return TryLock(_timeout, ct);
    }

    /// <summary>
    /// 尝试同步获取锁,指定超时覆盖实例默认超时。<see cref="TimeSpan.Zero"/> 为非阻塞尝试。
    /// </summary>
    public IDisposable? TryLock(TimeSpan timeout, CancellationToken ct = default) {
        ThrowIfDisposed();
        EnsureFlowRegistered();
        LockRegistry.OnWaitStart(_registryId, _name);
        bool acquired;
        try {
            acquired = _semaphore.Wait((int)timeout.TotalMilliseconds, ct);
        } catch (OperationCanceledException) {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        if (!acquired) {
            LockRegistry.OnWaitEnd(_registryId, _name);
            LockRegistry.OnLockTimeout(_name, timeout);
            return null;
        }
        LockRegistry.OnAcquired(_registryId, _name);
        return new Releaser(this);
    }

    /// <summary>
    /// 尝试异步获取锁。成功返回 Releaser,超时返回 null,取消抛 <see cref="OperationCanceledException"/>。
    /// async 方法中用此方法避免 <c>TryLock</c> 同步阻塞线程池导致饥饿。
    /// </summary>
    public async ValueTask<IDisposable?> TryLockAsync(CancellationToken ct = default) {
        return await TryLockAsync(_timeout, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 尝试异步获取锁,指定超时覆盖实例默认超时。
    /// </summary>
    public async ValueTask<IDisposable?> TryLockAsync(TimeSpan timeout, CancellationToken ct = default) {
        ThrowIfDisposed();
        EnsureFlowRegistered();
        LockRegistry.OnWaitStart(_registryId, _name);
        bool acquired;
        try {
            acquired = await _semaphore.WaitAsync((int)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        if (!acquired) {
            LockRegistry.OnWaitEnd(_registryId, _name);
            LockRegistry.OnLockTimeout(_name, timeout);
            return null;
        }
        LockRegistry.OnAcquired(_registryId, _name);
        return new Releaser(this);
    }

    /// <summary>
    /// 锁等待重试次数 — 与 <c>BackpressureWriter.MaxRetries</c> / <c>ActorBase.BackpressureMaxRetries</c> 对齐。
    /// </summary>
    public const int LockRetryCount = 16;

    /// <summary>
    /// 锁等待单次重试超时 — 500ms,16 次共 8s,与 <c>MailboxBase.WaitForCommandsDrainedAsync</c> 对齐。
    /// </summary>
    public static readonly TimeSpan LockRetryTimeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// 尝试同步获取锁,内置重试 16 次 × 500ms — 锁持有者卡在 I/O 时给予恢复窗口,全失败按 <see cref="_timeoutPolicy"/> 处理。
    /// <para>与 <see cref="TryLock(TimeSpan, CancellationToken)"/> 区别:单次超时 500ms,重试 16 次,总等待 8s。</para>
    /// <para>策略:Throw=抛 TimeoutException,Crash=Environment.Exit(99),Degrade=返回 null。</para>
    /// </summary>
    public IDisposable? TryLockWithRetry(CancellationToken ct = default) {
        for (var i = 0; i < LockRetryCount; i++) {
            var releaser = TryLock(LockRetryTimeout, ct);
            if (releaser is not null) return releaser;
        }
        return OnLockRetryExhausted();
    }

    /// <summary>
    /// 尝试异步获取锁,内置重试 16 次 × 500ms — 锁持有者卡在 I/O 时给予恢复窗口,全失败按 <see cref="_timeoutPolicy"/> 处理。
    /// <para>async 方法中用此方法避免 <c>TryLockWithRetry</c> 同步阻塞线程池。</para>
    /// </summary>
    public async ValueTask<IDisposable?> TryLockWithRetryAsync(CancellationToken ct = default) {
        for (var i = 0; i < LockRetryCount; i++) {
            var releaser = await TryLockAsync(LockRetryTimeout, ct).ConfigureAwait(false);
            if (releaser is not null) return releaser;
        }
        return OnLockRetryExhausted();
    }

    /// <summary>
    /// 锁重试耗尽后的策略处理 — Throw 抛异常,Crash 强制退出,Degrade 返回 null。
    /// </summary>
    private IDisposable? OnLockRetryExhausted() {
        switch (_timeoutPolicy) {
            case LockTimeoutPolicy.Throw:
                throw new TimeoutException(
                    $"锁 '{_name}' 重试{LockRetryCount}次×{LockRetryTimeout.TotalMilliseconds:F0}ms全失败");
            case LockTimeoutPolicy.Crash:
                AsyncStderrWriter.Enqueue(
                    $"[LOCK-FATAL] 锁 '{_name}' 重试{LockRetryCount}次×{LockRetryTimeout.TotalMilliseconds:F0}ms全失败,系统不可恢复,强制退出(99)");
                AsyncStderrWriter.Flush();
                Environment.Exit(99);
                return null;
            default:
                return null;
        }
    }

    /// <summary>
    /// 获取锁,重试 16 次×500ms,全失败按 <see cref="_timeoutPolicy"/> 处理 — Throw/Crash 策略下永不返回 null。
    /// <para>替代 <c>TryLock() ?? throw TimeoutException</c> 模式,内置重试+策略处理。</para>
    /// </summary>
    public IDisposable LockOrCrash(CancellationToken ct = default) {
        return TryLockWithRetry(ct)
            ?? throw new InvalidOperationException($"unreachable: {_name} {_timeoutPolicy} 策略不返回 null");
    }

    /// <summary>
    /// 异步获取锁,重试 16 次×500ms,全失败按 <see cref="_timeoutPolicy"/> 处理 — Throw/Crash 策略下永不返回 null。
    /// </summary>
    public async ValueTask<IDisposable> LockOrCrashAsync(CancellationToken ct = default) {
        return await TryLockWithRetryAsync(ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"unreachable: {_name} {_timeoutPolicy} 策略不返回 null");
    }

    /// <summary>
    /// 释放底层信号量并从 <see cref="LockRegistry"/> 注销。
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        LockRegistry.Unregister(_registryId);
        _semaphore.Dispose();
    }

    private void ThrowIfDisposed() {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
            throw new ObjectDisposedException(nameof(AsyncLock), $"锁 '{_name}' 已释放");
    }

    private sealed class Releaser(AsyncLock owner) : IDisposable {
        private int _disposed;
        /// <summary>释放资源。</summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                LockRegistry.OnReleased(owner._registryId, owner._name);
                if (Interlocked.CompareExchange(ref owner._disposed, 0, 0) == 0)
                    owner._semaphore.Release();
            }
        }
    }
}