namespace Infrastructure.Persistence;

/// <summary>
/// PithosDB 租约工厂 — AcquireAsync 时 Open PithosDb + 获取两锁联合，锁被占时降级内存。
/// <para>第1层：进程内 SemaphoreSlim(1,1) — 不可重入，同线程第二次获取阻塞（防重入）。</para>
/// <para>第2层：跨进程 CrossProcessLock — Windows 用 NamedMutex，Linux 用 flock，已处理 ReleaseMutex ApplicationException。</para>
/// <para>获取顺序：先 SemaphoreSlim 再 CrossProcessLock；释放顺序相反（lease.Dispose 负责）。</para>
/// <para>非阻塞获取：锁被占时立即降级 OpenInMemory，不等待。</para>
/// <para>复用 NamedMutexMailboxLock 两锁联合模式（ADR 0132）。</para>
/// </summary>
public sealed class PithosKvStoreLeaseFactory : IKvStoreLeaseFactory {
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_semaphores = new();

    private readonly string _dataDirectory;
    private readonly PithosOptions? _options;

    /// <summary>创建磁盘租约工厂。</summary>
    /// <param name="dataDirectory">数据目录路径。</param>
    /// <param name="options">可选 PithosDB 配置。</param>
    public PithosKvStoreLeaseFactory(string dataDirectory, PithosOptions? options = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = dataDirectory;
        _options = options;
    }

    /// <inheritdoc />
    public async Task<IKvStoreLease> AcquireAsync() {
        var lockKey = Path.GetFullPath(_dataDirectory);
        var semaphore = s_semaphores.GetOrAdd(lockKey, _ => new SemaphoreSlim(1, 1));

        if (!semaphore.Wait(0)) {
            return new PithosKvStoreLease(PithosDb.OpenInMemory(), null, null, ownsDb: true, isInMemory: true);
        }

        CrossProcessLock? crossProcessLock = null;
        try {
            crossProcessLock = await CrossProcessLock.AcquireAsync(lockKey, TimeSpan.Zero, CancellationToken.None).ConfigureAwait(false);
        } catch (TimeoutException) {
            semaphore.Release();
            return new PithosKvStoreLease(PithosDb.OpenInMemory(), null, null, ownsDb: true, isInMemory: true);
        }

        try {
            var db = _options is not null
                ? PithosDb.Open(_dataDirectory, _options)
                : PithosDb.Open(_dataDirectory);
            return new PithosKvStoreLease(db, crossProcessLock, semaphore, ownsDb: true, isInMemory: false);
        } catch (Exception ex) {
            Console.Error.WriteLine($"[PithosKvStoreLeaseFactory] Open failed, releasing locks: {ex.Message}");
            crossProcessLock.DisposeSafe();
            semaphore.Release();
            throw;
        }
    }

    /// <summary>计算数据目录对应的跨进程锁名 — 供测试占住锁验证降级路径。</summary>
    public static string GetMutexName(string dataDirectory) {
        var normalized = Path.GetFullPath(dataDirectory);
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return $@"Global\jcc-pithos-{Convert.ToHexString(hash, 0, 16)}";
    }
}

/// <summary>
/// 内存租约工厂 — 总是返回共享内存租约，不获取锁。用于 JCC_FILE_SYSTEM_MODE=InMemory。
/// <para>共享单个 PithosDb.OpenInMemory 实例 — 同工厂的所有 lease 数据共享（ownsDb=false，lease Dispose 不释放共享 db）。</para>
/// </summary>
public sealed class InMemoryKvStoreLeaseFactory : IKvStoreLeaseFactory {
    private readonly PithosDb _db = PithosDb.OpenInMemory();

    /// <inheritdoc />
    public Task<IKvStoreLease> AcquireAsync() =>
        Task.FromResult<IKvStoreLease>(new PithosKvStoreLease(_db, null, null, ownsDb: false, isInMemory: true));
}
