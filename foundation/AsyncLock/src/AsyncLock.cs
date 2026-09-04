namespace Core.Utils;

/// <summary>
/// 锁/并发限流原语 — SemaphoreSlim 的薄封装,支持互斥 (1,1) 和并发限流 (N,N) 两种语义。
/// 提供同步 <c>TryLock</c> 和异步 <c>TryLockAsync</c>。
/// async 方法中用 <c>TryLockAsync</c> 避免线程池饥饿;非 async 上下文用 <c>TryLock</c>。
/// 默认5s超时(可经 <see cref="AsyncLock(string, TimeSpan)"/> 构造按实例配置),超时返回 null 并记录日志,取消抛 OperationCanceledException。
/// 内部接入 <see cref="LockRegistry"/> 诊断:获取/释放时记录调用栈、线程、时间,卡死时调用 <see cref="LockRegistry.DumpAll"/> 精确定位。
/// </summary>
public sealed class AsyncLock : IDisposable
{
    /// <summary>
    /// 默认锁等待超时 — <see cref="TryLock(CancellationToken)"/> 在 ct 无超时时使用此值。
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _semaphore;
    private readonly string _name;
    private readonly TimeSpan _timeout;
    private readonly int _registryId;
    private int _disposed;

    /// <summary>
    /// 锁名称 — 诊断输出中显示此名称,便于定位"哪个环节"的锁。
    /// </summary>
    public string Name => _name;

    /// <summary>
    /// 构造匿名锁。锁名自动生成 <c>AsyncLock#{n}</c>,诊断时用调用栈定位。超时用 <see cref="DefaultTimeout"/>。
    /// </summary>
    public AsyncLock()
    {
        _semaphore = new SemaphoreSlim(1, 1);
        _name = $"AsyncLock#{LockRegistry.Count + 1}";
        _timeout = DefaultTimeout;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名锁 — 诊断输出中显示此名称,便于定位"哪个环节"的锁。超时用 <see cref="DefaultTimeout"/>。
    /// </summary>
    public AsyncLock(string name)
    {
        _semaphore = new SemaphoreSlim(1, 1);
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = DefaultTimeout;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名锁并指定超时 — 不同锁可独立配置超时(IO 密集锁可设 30s,内存状态锁 5s)。
    /// </summary>
    public AsyncLock(string name, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "超时必须为正值。");
        _semaphore = new SemaphoreSlim(1, 1);
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = timeout;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造并发限流锁 — (1,1) 为互斥,(N,N) 为并发限流。签名与 <see cref="SemaphoreSlim"/> 一致,降低迁移成本。
    /// </summary>
    public AsyncLock(int initialCount, int maxCount)
    {
        if (initialCount < 0 || maxCount < 1 || initialCount > maxCount)
            throw new ArgumentOutOfRangeException(
                nameof(initialCount),
                "initialCount 必须 >= 0,maxCount 必须 >= 1,且 initialCount <= maxCount。");
        _semaphore = new SemaphoreSlim(initialCount, maxCount);
        _name = $"AsyncLock#{LockRegistry.Count + 1}";
        _timeout = DefaultTimeout;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名并发限流锁 — (1,1) 为互斥,(N,N) 为并发限流。签名与 <see cref="SemaphoreSlim"/> 一致,同时支持具名诊断。
    /// </summary>
    public AsyncLock(string name, int initialCount, int maxCount)
    {
        if (initialCount < 0 || maxCount < 1 || initialCount > maxCount)
            throw new ArgumentOutOfRangeException(
                nameof(initialCount),
                "initialCount 必须 >= 0,maxCount 必须 >= 1,且 initialCount <= maxCount。");
        _semaphore = new SemaphoreSlim(initialCount, maxCount);
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = DefaultTimeout;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名并发限流锁并指定超时 — (1,1) 为互斥,(N,N) 为并发限流。
    /// </summary>
    public AsyncLock(string name, int initialCount, int maxCount, TimeSpan timeout)
    {
        if (initialCount < 0 || maxCount < 1 || initialCount > maxCount)
            throw new ArgumentOutOfRangeException(
                nameof(initialCount),
                "initialCount 必须 >= 0,maxCount 必须 >= 1,且 initialCount <= maxCount。");
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "超时必须为正值。");
        _semaphore = new SemaphoreSlim(initialCount, maxCount);
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = timeout;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 尝试同步获取锁。成功返回 Releaser,超时返回 null 并通过 <see cref="LockRegistry"/> 记录日志,取消抛 <see cref="OperationCanceledException"/>。
    /// <para>超时规则:若 <paramref name="ct"/> 可被取消(调用方已绑定超时),使用调用方的 ct;否则使用 <see cref="DefaultTimeout"/>(5s)。</para>
    /// </summary>
    public IDisposable? TryLock(CancellationToken ct = default)
    {
        return TryLock(_timeout, ct);
    }

    /// <summary>
    /// 尝试同步获取锁,指定超时覆盖实例默认超时。<see cref="TimeSpan.Zero"/> 为非阻塞尝试。
    /// </summary>
    public IDisposable? TryLock(TimeSpan timeout, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        LockRegistry.OnWaitStart(_registryId, _name);
        bool acquired;
        try
        {
            acquired = _semaphore.Wait((int)timeout.TotalMilliseconds, ct);
        }
        catch (OperationCanceledException)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        if (!acquired)
        {
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
    public async ValueTask<IDisposable?> TryLockAsync(CancellationToken ct = default)
    {
        return await TryLockAsync(_timeout, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 尝试异步获取锁,指定超时覆盖实例默认超时。
    /// </summary>
    public async ValueTask<IDisposable?> TryLockAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        LockRegistry.OnWaitStart(_registryId, _name);
        bool acquired;
        try
        {
            acquired = await _semaphore.WaitAsync((int)timeout.TotalMilliseconds, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        if (!acquired)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            LockRegistry.OnLockTimeout(_name, timeout);
            return null;
        }
        LockRegistry.OnAcquired(_registryId, _name);
        return new Releaser(this);
    }

    /// <summary>
    /// 释放底层信号量并从 <see cref="LockRegistry"/> 注销。
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        LockRegistry.Unregister(_registryId);
        _semaphore.Dispose();
    }

    private void ThrowIfDisposed()
    {
        if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0)
            throw new ObjectDisposedException(nameof(AsyncLock), $"锁 '{_name}' 已释放");
    }

    private sealed class Releaser(AsyncLock owner) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                LockRegistry.OnReleased(owner._registryId, owner._name);
                owner._semaphore.Release();
            }
        }
    }
}
