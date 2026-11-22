namespace JoinCode.Persistence.KvStore;

/// <summary>
/// PithosDB 租约工厂 — AcquireAsync 时 Open PithosDb + 获取两层租约，租约被占时降级内存。
/// <para>两层互斥由 <see cref="NamedMutexMailboxLock"/> 封装：进程内原子占位（同进程防重入）+ 跨进程原子文件租约（CreateNew + token + TTL + 心跳）。</para>
/// <para>获取顺序：先进程内占位再跨进程租约；释放顺序相反（lease.Dispose 负责）。</para>
/// <para>非阻塞获取：租约被任一层占用时立即降级 OpenInMemory，不等待；过期租约当场接管。</para>
/// </summary>
public sealed class PithosKvStoreLeaseFactory : IKvStoreLeaseFactory {
    private readonly string _dataDirectory;
    private readonly PithosOptions? _options;
    private readonly ILogger? _logger;

    /// <summary>创建磁盘租约工厂。</summary>
    /// <param name="dataDirectory">数据目录路径。</param>
    /// <param name="options">可选 PithosDB 配置。</param>
    /// <param name="logger">可选日志器（记录租约获取/降级时间线）。</param>
    public PithosKvStoreLeaseFactory(string dataDirectory, PithosOptions? options = null, ILogger? logger = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _dataDirectory = dataDirectory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IKvStoreLease> AcquireAsync() {
        var lockKey = Path.GetFullPath(_dataDirectory);

        NamedMutexMailboxLock? mailboxLock;
        try {
            mailboxLock = await NamedMutexMailboxLock.AcquireAsync(lockKey, TimeSpan.Zero, CancellationToken.None, _logger).ConfigureAwait(false);
        } catch (TimeoutException ex) {
            _logger?.LogDebug("[PithosKvStoreLeaseFactory] 租约已占用，降级内存: {LockKey} ({Reason})", lockKey, ex.Message);
            return CreateInMemoryLease();
        }

        try {
            var db = _options is not null
                ? PithosDb.Open(_dataDirectory, _options)
                : PithosDb.Open(_dataDirectory);
            return new PithosKvStoreLease(db, mailboxLock, ownsDb: true, isInMemory: false);
        } catch (Exception ex) {
            _logger?.LogError(ex, "[PithosKvStoreLeaseFactory] PithosDb 打开失败，释放租约: {LockKey}", lockKey);
            try {
                mailboxLock.Release();
            } catch (Exception releaseEx) {
                _logger?.LogWarning(releaseEx, "[PithosKvStoreLeaseFactory] 打开失败后释放租约失败: {LockKey}", lockKey);
            }
            throw;
        }
    }

    private static PithosKvStoreLease CreateInMemoryLease()
        => new(PithosDb.OpenInMemory(), null, ownsDb: true, isInMemory: true);
}

/// <summary>
/// 内存租约工厂 — 总是返回共享内存租约，不获取锁。用于 JCC_FILE_SYSTEM_MODE=InMemory。
/// <para>共享单个 PithosDb.OpenInMemory 实例 — 同工厂的所有 lease 数据共享（ownsDb=false，lease Dispose 不释放共享 db）。</para>
/// </summary>
public sealed class InMemoryKvStoreLeaseFactory : IKvStoreLeaseFactory {
    private readonly PithosDb _db = PithosDb.OpenInMemory();

    /// <inheritdoc />
    public Task<IKvStoreLease> AcquireAsync() =>
        Task.FromResult<IKvStoreLease>(new PithosKvStoreLease(_db, null, ownsDb: false, isInMemory: true));
}
