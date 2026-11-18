namespace AsyncFileLock;

/// <summary>
/// 跨进程邮箱锁 — 进程内占位 + 跨进程原子文件租约双重互斥。
/// <para>第1层：<see cref="InProcessLeaseRegistry"/> 原子占位 — 同进程并发获取按退避轮询排队，替代原 SemaphoreSlim(1,1)。</para>
/// <para>第2层：<see cref="CrossProcessLock"/> 原子文件租约（CreateNew + token + TTL + 心跳）— 跨进程互斥，进程崩溃后 TTL 到期可接管。</para>
/// <para>获取顺序：先进程内占位再跨进程租约；释放顺序相反。</para>
/// <para>日志：获取/释放记录 token 与租约文件路径，多进程竞争时间线可审计。</para>
/// </summary>
public sealed class NamedMutexMailboxLock : IAsyncDisposable {
    private readonly CrossProcessLock _lease;
    private readonly string _registryKey;
    private readonly ILogger? _logger;
    private int _disposed;

    /// <summary>已锁定的文件绝对路径</summary>
    public string FilePath { get; }

    private NamedMutexMailboxLock(CrossProcessLock lease, string registryKey, string filePath, ILogger? logger) {
        _lease = lease;
        _registryKey = registryKey;
        FilePath = filePath;
        _logger = logger;
    }

    /// <summary>
    /// 异步获取指定文件路径的锁，超时未获取则抛出 <see cref="TimeoutException"/>。
    /// </summary>
    /// <param name="filePath">要锁定的文件路径</param>
    /// <param name="timeout">获取锁的超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="fs">可选文件系统（默认 PhysicalFileSystem，经 FileSystemFactory）</param>
    /// <returns>已获取的文件锁实例</returns>
    public static async Task<NamedMutexMailboxLock> AcquireAsync(
        string filePath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        ILogger? logger = null,
        IFileSystem? fs = null) {
        var fullPath = Path.GetFullPath(filePath);
        var lockKey = OperatingSystem.IsWindows() ? fullPath.ToLowerInvariant() : fullPath;

        await InProcessLeaseRegistry.ClaimAsync(lockKey, timeout, cancellationToken).ConfigureAwait(false);

        try {
            var lease = await CrossProcessLock.AcquireAsync(lockKey, timeout, cancellationToken, logger, fs).ConfigureAwait(false);
            logger?.LogDebug("NamedMutexMailboxLock acquired: {FilePath} token={Token}", fullPath, lease.Token);
            return new NamedMutexMailboxLock(lease, lockKey, fullPath, logger);
        } catch {
            InProcessLeaseRegistry.Release(lockKey);
            throw;
        }
    }

    /// <summary>
    /// 异步释放锁 — 先释放跨进程租约再释放进程内占位
    /// </summary>
    public ValueTask DisposeAsync() {
        Release();
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 同步释放锁，语义与 DisposeAsync 等价
    /// </summary>
    internal void Release() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _lease.Dispose();
        InProcessLeaseRegistry.Release(_registryKey);
        _logger?.LogDebug("NamedMutexMailboxLock released: {FilePath}", FilePath);
    }
}
