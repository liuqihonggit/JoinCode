namespace Core.Utils;

/// <summary>
/// 异步互斥锁 — 项目唯一互斥锁原语，所有互斥场景统一走此类型。
/// 内部接入 <see cref="LockRegistry"/> 诊断：获取/释放时记录调用栈、线程、时间，
/// 卡死时调用 <see cref="LockRegistry.DumpAll"/> 精确定位。性能非首要目标，诊断能力优先。
/// </summary>
public sealed class AsyncLock : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly string _name;
    private readonly int _registryId;
    private int _disposed;

    /// <summary>
    /// 构造匿名锁（向后兼容）。锁名自动生成 <c>AsyncLock#{n}</c>，诊断时用调用栈定位。
    /// </summary>
    public AsyncLock()
    {
        _name = $"AsyncLock#{LockRegistry.Count + 1}";
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名锁 — 诊断输出中显示此名称，便于定位"哪个环节"的锁。
    /// </summary>
    public AsyncLock(string name)
    {
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _registryId = LockRegistry.Register(_name);
    }

    public AsyncLock(int initialCount, int maxCount)
    {
        if (initialCount != 1 || maxCount != 1)
            throw new ArgumentOutOfRangeException(
                nameof(initialCount),
                "AsyncLock 仅支持互斥语义 (1,1)。信号量/并发限流请使用 SemaphoreSlim。");
        _name = $"AsyncLock#{LockRegistry.Count + 1}";
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 异步获取锁。获取/释放全程由 <see cref="LockRegistry"/> 记录诊断信息。
    /// </summary>
    public async ValueTask<IDisposable> LockAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        LockRegistry.OnWaitStart(_registryId, _name);
        try
        {
            await _semaphore.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        LockRegistry.OnAcquired(_registryId, _name);
        return new Releaser(this);
    }

    /// <summary>
    /// 异步获取锁(带超时)。超时抛 TimeoutException。对齐 SemaphoreSlim.WaitAsync(timeout, ct)。
    /// </summary>
    public async ValueTask<IDisposable> LockAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        LockRegistry.OnWaitStart(_registryId, _name);
        bool acquired;
        try
        {
            acquired = await _semaphore.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        if (!acquired)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw new TimeoutException($"AsyncLock '{_name}' 等待超时 {timeout}");
        }
        LockRegistry.OnAcquired(_registryId, _name);
        return new Releaser(this);
    }

    /// <summary>
    /// 尝试异步获取锁(带超时)。成功返回 Releaser,超时返回 null。对齐 SemaphoreSlim.WaitAsync(timeout) 返回 bool。
    /// </summary>
    public async ValueTask<IDisposable?> TryLockAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        ThrowIfDisposed();
        LockRegistry.OnWaitStart(_registryId, _name);
        bool acquired;
        try
        {
            acquired = await _semaphore.WaitAsync(timeout, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        if (!acquired)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            return null;
        }
        LockRegistry.OnAcquired(_registryId, _name);
        return new Releaser(this);
    }

    /// <summary>
    /// 同步获取锁。对齐原 SemaphoreSlim.Wait()。
    /// </summary>
    public IDisposable Lock()
    {
        ThrowIfDisposed();
        LockRegistry.CheckReentrancy(_registryId, _name);
        LockRegistry.OnWaitStart(_registryId, _name);
        try
        {
            _semaphore.Wait();
        }
        catch
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        LockRegistry.OnAcquired(_registryId, _name);
        return new Releaser(this);
    }

    /// <summary>
    /// 同步获取锁(带 CancellationToken)。对齐 SemaphoreSlim.Wait(ct)。
    /// </summary>
    public IDisposable Lock(CancellationToken ct)
    {
        ThrowIfDisposed();
        LockRegistry.CheckReentrancy(_registryId, _name);
        LockRegistry.OnWaitStart(_registryId, _name);
        try
        {
            _semaphore.Wait(ct);
        }
        catch (OperationCanceledException)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        LockRegistry.OnAcquired(_registryId, _name);
        return new Releaser(this);
    }

    /// <summary>
    /// 尝试同步获取锁(非阻塞)。成功返回 Releaser,失败返回 null。对齐 SemaphoreSlim.Wait(0) 语义。
    /// </summary>
    public IDisposable? TryLock()
    {
        ThrowIfDisposed();
        LockRegistry.OnWaitStart(_registryId, _name);
        var acquired = _semaphore.Wait(0);
        if (!acquired)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            return null;
        }
        LockRegistry.OnAcquired(_registryId, _name);
        return new Releaser(this);
    }

    /// <summary>
    /// 尝试同步获取锁(带超时)。成功返回 Releaser,超时返回 null。对齐 SemaphoreSlim.Wait(timeout)。
    /// </summary>
    public IDisposable? TryLock(TimeSpan timeout)
    {
        ThrowIfDisposed();
        LockRegistry.OnWaitStart(_registryId, _name);
        bool acquired;
        try
        {
            acquired = _semaphore.Wait(timeout);
        }
        catch (OperationCanceledException)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        if (!acquired)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
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
