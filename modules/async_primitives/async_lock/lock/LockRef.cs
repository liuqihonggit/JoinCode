namespace Core.Utils;

/// <summary>
/// 锁借用句柄 — 非 IDisposable,持有 <see cref="AsyncLock"/> 引用但不拥有所有权。
/// 所有权由创建 AsyncLock 的容器持有(字段/字典),LockRef 仅转发锁操作。
/// 方案 D「写法即语义」:<c>GetLock</c> 返回 <see cref="LockRef"/> 而非 <see cref="AsyncLock"/>,
/// 使借用源不触发 IDisposable 泄露检测(JCC9305)。调用方 <c>lk.TryLock()</c> 不变。
/// </summary>
public readonly struct LockRef {
    private readonly AsyncLock _lock;

    /// <summary>构造锁借用句柄,引用目标锁(不获取所有权)。</summary>
    /// <param name="lock">被借用的锁,不可为 null。</param>
    public LockRef(AsyncLock @lock) {
        _lock = @lock ?? throw new ArgumentNullException(nameof(@lock));
    }

    /// <summary>锁名称(转发至 <see cref="AsyncLock.Name"/>,用于诊断/超时报错)。</summary>
    public string Name => _lock.Name;

    /// <summary>尝试同步获取锁,成功返回 IDisposable 守卫(释放即归还),超时返回 null。</summary>
    public IDisposable? TryLock(CancellationToken ct = default) => _lock.TryLock(ct);

    /// <summary>尝试同步获取锁,指定超时覆盖实例默认超时。<see cref="TimeSpan.Zero"/> 为非阻塞尝试。</summary>
    public IDisposable? TryLock(TimeSpan timeout, CancellationToken ct = default) => _lock.TryLock(timeout, ct);

    /// <summary>尝试异步获取锁,成功返回 IDisposable 守卫,超时返回 null。</summary>
    public ValueTask<IDisposable?> TryLockAsync(CancellationToken ct = default) => _lock.TryLockAsync(ct);

    /// <summary>尝试同步获取锁,失败时按诊断配置重试(转发)。</summary>
    public IDisposable? TryLockWithRetry(CancellationToken ct = default) => _lock.TryLockWithRetry(ct);

    /// <summary>获取锁,超时抛 <see cref="System.TimeoutException"/>(转发)。</summary>
    public IDisposable LockOrCrash(CancellationToken ct = default) => _lock.LockOrCrash(ct);
}
