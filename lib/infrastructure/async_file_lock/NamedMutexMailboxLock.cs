namespace AsyncFileLock;

/// <summary>
/// 跨进程命名 Mutex 邮箱锁 — 多层锁实现进程内 + 跨进程双重互斥。
/// <para>第1层：进程内 SemaphoreSlim(1,1) — 不可重入，同线程第二次获取会阻塞超时（替代原文件锁的不可重入语义）。</para>
/// <para>第2层：跨进程命名 Mutex — 内核对象，OS 保证原子性，杀毒软件不干预。</para>
/// <para>获取顺序：先 SemaphoreSlim（进程内）再 Mutex（跨进程）；释放顺序相反。</para>
/// <para>进程崩溃 = OS 自动回收 Mutex 内核对象，下一个等待者收到 AbandonedMutexException 并视为获取成功。</para>
/// <para>替代旧 FileMailboxLock：文件锁（FileMode.CreateNew+DeleteFile）在 Windows 上被杀毒软件拦截锁文件删除，导致 flaky。</para>
/// </summary>
public sealed class NamedMutexMailboxLock : IAsyncDisposable {
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_semaphores = new();

    private readonly Mutex _mutex;
    private readonly SemaphoreSlim _semaphore;
    private readonly ILogger? _logger;
    private int _disposed;

    /// <summary>已锁定的文件绝对路径</summary>
    public string FilePath { get; }

    private NamedMutexMailboxLock(Mutex mutex, SemaphoreSlim semaphore, string filePath, ILogger? logger) {
        _mutex = mutex;
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
        var lockKey = fullPath.ToLowerInvariant();
        var semaphore = s_semaphores.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        if (!await semaphore.WaitAsync(timeout, cancellationToken).ConfigureAwait(false)) {
            throw new TimeoutException($"Failed to acquire in-process lock for '{filePath}' within {timeout.TotalSeconds}s");
        }

        var mutexName = GetMutexName(lockKey);
        var mutex = CreateMutex(mutexName);
        try {
            var acquired = await WaitOneAsync(mutex, timeout, cancellationToken).ConfigureAwait(false);
            if (!acquired) {
                throw new TimeoutException($"Failed to acquire cross-process lock for '{filePath}' within {timeout.TotalSeconds}s");
            }
            logger?.LogDebug("NamedMutexMailboxLock acquired: {FilePath} -> {MutexName}", fullPath, mutexName);
            return new NamedMutexMailboxLock(mutex, semaphore, fullPath, logger);
        } catch {
            mutex.Dispose();
            semaphore.Release();
            throw;
        }
    }

    /// <summary>
    /// 用 Task.Run 包装同步 WaitOne，避免阻塞调用线程
    /// <para>AbandonedMutexException: 前一持有者进程崩溃，OS 将锁交给我们，视为获取成功</para>
    /// </summary>
    private static Task<bool> WaitOneAsync(Mutex mutex, TimeSpan timeout, CancellationToken ct) {
        return Task.Run(() => {
            try { return mutex.WaitOne(timeout); }
            catch (AbandonedMutexException) { return true; }
        }, ct);
    }

    /// <summary>
    /// 创建命名 Mutex — 优先 Global 作用域（全机器），权限不足回退 Local（per-session）
    /// <para>文件是全机器可见的，故 Mutex 亦应全机器作用域。</para>
    /// <para>非交互式服务账户可能无 SeCreateGlobalPrivilege，回退 Local 保证可用。</para>
    /// </summary>
    private static Mutex CreateMutex(string name) {
        try {
            return new Mutex(initiallyOwned: false, name: "Global\\" + name, createdNew: out _);
        } catch (Exception ex) when (ex is UnauthorizedAccessException or IOException) {
            return new Mutex(initiallyOwned: false, name: "Local\\" + name, createdNew: out _);
        }
    }

    private static string GetMutexName(string lockKey) {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(lockKey)));
        return "jcc_mailbox_" + hash;
    }

    /// <summary>
    /// 异步释放锁 — 先释放 Mutex（跨进程）再释放 SemaphoreSlim（进程内）
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

        try {
            _mutex.ReleaseMutex();
        } catch (ApplicationException ex) {
            _logger?.LogWarning(ex, "NamedMutexMailboxLock: failed to release mutex for {FilePath}", FilePath);
        }
        _mutex.Dispose();

        _semaphore.Release();
    }
}
