namespace Core.Utils;

/// <summary>
/// 互斥锁 — SemaphoreSlim(1,1) 的薄封装,项目唯一互斥锁原语。
/// 提供同步 <see cref="TryLock(CancellationToken)"/> 和异步 <see cref="TryLockAsync(CancellationToken)"/>。
/// async 方法中用 <see cref="TryLockAsync"/> 避免线程池饥饿;非 async 上下文用 <see cref="TryLock"/>。
/// 默认5s超时(可经 <see cref="AsyncLock(string, TimeSpan)"/> 构造按实例配置),超时返回 null 并记录日志,取消抛 OperationCanceledException。
/// 内部接入 <see cref="LockRegistry"/> 诊断:获取/释放时记录调用栈、线程、时间,卡死时调用 <see cref="LockRegistry.DumpAll"/> 精确定位。
/// </summary>
public sealed class AsyncLock : IDisposable
{
    /// <summary>
    /// 默认锁等待超时 — <see cref="TryLock(CancellationToken)"/> 在 ct 无超时时使用此值。
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly SemaphoreSlim _semaphore = new(1, 1);
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
        _name = $"AsyncLock#{LockRegistry.Count + 1}";
        _timeout = DefaultTimeout;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名锁 — 诊断输出中显示此名称,便于定位"哪个环节"的锁。超时用 <see cref="DefaultTimeout"/>。
    /// </summary>
    public AsyncLock(string name)
    {
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
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = timeout;
        _registryId = LockRegistry.Register(_name);
    }

    public AsyncLock(int initialCount, int maxCount)
    {
        if (initialCount != 1 || maxCount != 1)
            throw new ArgumentOutOfRangeException(
                nameof(initialCount),
                "AsyncLock 仅支持互斥语义 (1,1)。信号量/并发限流请使用 SemaphoreSlim。");
        _name = $"AsyncLock#{LockRegistry.Count + 1}";
        _timeout = DefaultTimeout;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 构造具名锁 + 参数兼容构造 — 签名与 <see cref="SemaphoreSlim"/> 一致,降低迁移成本,同时支持具名诊断。
    /// </summary>
    public AsyncLock(string name, int initialCount, int maxCount)
    {
        if (initialCount != 1 || maxCount != 1)
            throw new ArgumentOutOfRangeException(
                nameof(initialCount),
                "AsyncLock 仅支持互斥语义 (1,1)。信号量/并发限流请使用 SemaphoreSlim。");
        _name = string.IsNullOrWhiteSpace(name) ? $"AsyncLock#{LockRegistry.Count + 1}" : name;
        _timeout = DefaultTimeout;
        _registryId = LockRegistry.Register(_name);
    }

    /// <summary>
    /// 尝试同步获取锁。成功返回 Releaser,超时返回 null 并通过 <see cref="LockRegistry"/> 记录日志,取消抛 <see cref="OperationCanceledException"/>。
    /// <para>超时规则:若 <paramref name="ct"/> 可被取消(调用方已绑定超时),使用调用方的 ct;否则使用 <see cref="DefaultTimeout"/>(5s)。</para>
    /// </summary>
    public IDisposable? TryLock(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        LockRegistry.OnWaitStart(_registryId, _name);
        bool acquired;
        try
        {
            acquired = _semaphore.Wait((int)_timeout.TotalMilliseconds, ct);
        }
        catch (OperationCanceledException)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        if (!acquired)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            LockRegistry.OnLockTimeout(_name, _timeout);
            return null;
        }
        LockRegistry.OnAcquired(_registryId, _name);
        return new Releaser(this);
    }

    /// <summary>
    /// 尝试异步获取锁。成功返回 Releaser,超时返回 null,取消抛 <see cref="OperationCanceledException"/>。
    /// async 方法中用此方法避免 <see cref="TryLock"/> 同步阻塞线程池导致饥饿。
    /// </summary>
    public async ValueTask<IDisposable?> TryLockAsync(CancellationToken ct = default)
    {
        ThrowIfDisposed();
        LockRegistry.OnWaitStart(_registryId, _name);
        bool acquired;
        try
        {
            acquired = await _semaphore.WaitAsync((int)_timeout.TotalMilliseconds, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            throw;
        }
        if (!acquired)
        {
            LockRegistry.OnWaitEnd(_registryId, _name);
            LockRegistry.OnLockTimeout(_name, _timeout);
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
