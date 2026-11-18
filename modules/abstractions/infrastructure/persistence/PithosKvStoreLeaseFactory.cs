namespace Infrastructure.Persistence;

/// <summary>
/// PithosDB 租约工厂 — AcquireAsync 时 Open PithosDb + 获取两层租约，租约被占时降级内存。
/// <para>第1层：<see cref="InProcessLeaseRegistry"/> 原子占位 — 非阻塞，同进程已占立即降级。</para>
/// <para>第2层：<see cref="CrossProcessLock"/> 原子文件租约（CreateNew + token + TTL + 心跳）— 非阻塞，被占立即降级；过期租约当场接管。</para>
/// <para>获取顺序：先进程内占位再跨进程租约；释放顺序相反（lease.Dispose 负责）。</para>
/// <para>非阻塞获取：租约被占时立即降级 OpenInMemory，不等待。</para>
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

        if (!InProcessLeaseRegistry.TryClaim(lockKey)) {
            _logger?.LogDebug("[PithosKvStoreLeaseFactory] 进程内租约已占用，降级内存: {LockKey}", lockKey);
            return CreateInMemoryLease();
        }

        CrossProcessLock? crossProcessLock = null;
        try {
            crossProcessLock = await CrossProcessLock.AcquireAsync(lockKey, TimeSpan.Zero, CancellationToken.None, _logger).ConfigureAwait(false);
        } catch (TimeoutException ex) {
            InProcessLeaseRegistry.Release(lockKey);
            _logger?.LogDebug("[PithosKvStoreLeaseFactory] 跨进程租约被占，降级内存: {LockKey} ({Reason})", lockKey, ex.Message);
            return CreateInMemoryLease();
        }

        try {
            var db = _options is not null
                ? PithosDb.Open(_dataDirectory, _options)
                : PithosDb.Open(_dataDirectory);
            return new PithosKvStoreLease(db, crossProcessLock, lockKey, ownsDb: true, isInMemory: false);
        } catch (Exception ex) {
            _logger?.LogError(ex, "[PithosKvStoreLeaseFactory] PithosDb 打开失败，释放租约: {LockKey}", lockKey);
            crossProcessLock.DisposeSafe(_logger);
            InProcessLeaseRegistry.Release(lockKey);
            throw;
        }
    }

    private static PithosKvStoreLease CreateInMemoryLease()
        => new(PithosDb.OpenInMemory(), null, null, ownsDb: true, isInMemory: true);

    /// <summary>计算数据目录对应的租约文件路径 — 供测试占住租约验证降级路径。</summary>
    public static string GetLeasePath(string dataDirectory)
        => CrossProcessLock.ResolveLeasePath(FileSystemFactory.Create(), Path.GetFullPath(dataDirectory));
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
