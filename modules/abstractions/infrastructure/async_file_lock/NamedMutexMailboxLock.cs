namespace AsyncFileLock;

/// <summary>
/// 跨进程命名 Mutex 邮箱锁 — 多层锁实现进程内 + 跨进程双重互斥。
/// <para>第1层：进程内 SemaphoreSlim(1,1) — 不可重入，同线程第二次获取会阻塞超时（替代原文件锁的不可重入语义）。</para>
/// <para>第2层：跨进程锁 — Windows 用 NamedMutex（内核对象），Linux 用 flock（POSIX 文件锁），委托 CrossProcessLock 实现。</para>
/// <para>获取顺序：先 SemaphoreSlim（进程内）再跨进程锁；释放顺序相反。</para>
/// <para>进程崩溃 = OS 自动回收锁资源，下一个等待者收到 AbandonedMutexException(Windows) 或文件锁释放(Linux) 并视为获取成功。</para>
/// <para>替代旧 FileMailboxLock：文件锁（FileMode.CreateNew+DeleteFile）在 Windows 上被杀毒软件拦截锁文件删除，导致 flaky。</para>
/// </summary>
public sealed class NamedMutexMailboxLock : IAsyncDisposable {
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_semaphores = new();

    private readonly CrossProcessLock _crossProcessLock;
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger? _logger;
    private int _disposed;

    /// <summary>已锁定的文件绝对路径</summary>
    public string FilePath { get; }

    private NamedMutexMailboxLock(CrossProcessLock crossProcessLock, SemaphoreSlim semaphore, string filePath, ILogger? logger) {
        _crossProcessLock = crossProcessLock;
        _semaphore = semaphore;
        FilePath = filePath;
        _logger = logger;
    }

    /// <summary>
    /// 异步获取指定文件路径的锁，超时未获取则抛出 TimeoutException
    /// </summary>
    /// <param name="filePath">要锁定的文件路径</param>
    /// <param name="timeout">获取锁的超时时间</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="logger">可选日志记录器</param>
    /// <returns>已获取的文件锁实例</returns>
    public static async Task<NamedMutexMailboxLock> AcquireAsync(
        string filePath,
        TimeSpan timeout,
        CancellationToken cancellationToken = default,
        ILogger? logger = null) {
        var fullPath = Path.GetFullPath(filePath);
        var lockKey = OperatingSystem.IsWindows() ? fullPath.ToLowerInvariant() : fullPath;
        var semaphore = s_semaphores.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        if (!await semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false)) {
            throw new TimeoutException($"Failed to acquire in-process lock for '{filePath}' within {timeout.TotalSeconds}s");
        }

        try {
            var crossProcessLock = await CrossProcessLock.AcquireAsync(lockKey, timeout, cancellationToken).ConfigureAwait(false);
            logger?.LogDebug("NamedMutexMailboxLock acquired: {FilePath}", fullPath);
            return new NamedMutexMailboxLock(crossProcessLock, semaphore, fullPath, logger);
        } catch {
            semaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// 异步释放锁 — 先释放跨进程锁再释放 SemaphoreSlim（进程内）
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

        _crossProcessLock.Dispose();
        _semaphore.Release();
    }
}
