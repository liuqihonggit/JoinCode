namespace Infrastructure.Persistence;

/// <summary>
/// PithosDB 租约工厂 — Acquire 时 Open PithosDb + 获取跨进程 Mutex，Mutex 被占时降级内存。
/// <para>跨进程 Mutex 名：Global\jcc-pithos-{SHA256(fullPath)[0:16]} — 按数据目录隔离。</para>
/// <para>非阻塞获取（WaitOne(0)）：Mutex 被占时立即降级 OpenInMemory，不等待。</para>
/// </summary>
public sealed class PithosKvStoreLeaseFactory : IKvStoreLeaseFactory {
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
    public IKvStoreLease Acquire() {
        var mutexName = GetMutexName(_dataDirectory);
        var mutex = new Mutex(false, mutexName, out _);
        if (mutex.WaitOne(0)) {
            try {
                var db = _options is not null
                    ? PithosDb.Open(_dataDirectory, _options)
                    : PithosDb.Open(_dataDirectory);
                return new PithosKvStoreLease(db, mutex, ownsDb: true, isInMemory: false);
            }
            catch (Exception ex) {
                Console.Error.WriteLine($"[PithosKvStoreLeaseFactory] Open failed, releasing mutex: {ex.Message}");
                try { mutex.ReleaseMutex(); }
                catch (Exception ex2) { Console.Error.WriteLine($"[PithosKvStoreLeaseFactory] ReleaseMutex failed: {ex2.Message}"); }
                mutex.Dispose();
                throw;
            }
        }
        mutex.Dispose();
        return new PithosKvStoreLease(PithosDb.OpenInMemory(), null, ownsDb: true, isInMemory: true);
    }

    /// <summary>计算数据目录对应的跨进程 Mutex 名 — 供测试占住 Mutex 验证降级路径。</summary>
    public static string GetMutexName(string dataDirectory) {
        var normalized = Path.GetFullPath(dataDirectory);
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = System.Security.Cryptography.SHA256.HashData(bytes);
        return $@"Global\jcc-pithos-{Convert.ToHexString(hash, 0, 16)}";
    }
}

/// <summary>
/// 内存租约工厂 — 总是返回内存租约，不获取 Mutex。用于 JCC_FILE_SYSTEM_MODE=InMemory。
/// </summary>
public sealed class InMemoryKvStoreLeaseFactory : IKvStoreLeaseFactory {
    /// <inheritdoc />
    public IKvStoreLease Acquire() =>
        new PithosKvStoreLease(PithosDb.OpenInMemory(), null, ownsDb: true, isInMemory: true);
}
