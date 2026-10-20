namespace Infrastructure.Persistence;

/// <summary>
/// 短租约 KV 存储适配器 — 每次 KV 操作独立租约(Acquire→执行→Dispose),遵守 ADR 0139 短租约协议。
/// <para>用途: 长生命周期 DI 单例(如 gh 缓存 GitHubToolHandlers/GhCacheService)消费 IKvStore 时,
/// 不持有进程级锁(长驻 mcp server 会饿死所有 CLI 进程→降级内存→缓存永远 miss),
/// 每次操作只占用几毫秒竞争窗口,CLI 与常驻进程可交替获得磁盘租约。</para>
/// <para>进程内 SemaphoreSlim 串行化: 防止同进程并发操作因 lease factory 防重入锁(SemaphoreSlim(1,1))
/// 拿不到而降级内存导致写入丢失(Actor 邮箱模型 — 操作排队而非并发竞争)。</para>
/// <para>TTL 扩展(KvStoreTtlExtensions)直接可用 — 适配器实现 IKvStore 四个核心方法,扩展方法自动转发。</para>
/// </summary>
public sealed class LeasedKvStoreAdapter : IKvStore {
    private readonly IKvStoreLeaseFactory _factory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>创建短租约适配器 — 每次操作从工厂 Acquire 短租约并转发</summary>
    /// <param name="factory">KV 存储租约工厂(磁盘/内存由工厂决定)</param>
    public LeasedKvStoreAdapter(IKvStoreLeaseFactory factory) {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public async ValueTask PutAsync(byte[] key, byte[] value, CancellationToken ct = default) {
        _gate.Wait(ct);
        try {
            using var lease = await _factory.AcquireAsync().ConfigureAwait(false);
            await lease.PutAsync(key, value, ct).ConfigureAwait(false);
        } finally {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken ct = default) {
        _gate.Wait(ct);
        try {
            using var lease = await _factory.AcquireAsync().ConfigureAwait(false);
            return await lease.GetAsync(key, ct).ConfigureAwait(false);
        } finally {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(byte[] key, CancellationToken ct = default) {
        _gate.Wait(ct);
        try {
            using var lease = await _factory.AcquireAsync().ConfigureAwait(false);
            await lease.DeleteAsync(key, ct).ConfigureAwait(false);
        } finally {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    /// <para>范围扫描在枚举期间持有租约(yield 需要底层 db 存活),枚举结束/中断释放 — try-finally 保证。</para>
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
        byte[]? from = null, byte[]? to = null, [EnumeratorCancellation] CancellationToken ct = default) {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try {
            using var lease = await _factory.AcquireAsync().ConfigureAwait(false);
            await foreach (var kv in lease.ScanAsync(from, to, ct).ConfigureAwait(false)) {
                yield return kv;
            }
        } finally {
            _gate.Release();
        }
    }

    /// <summary>释放进程内串行门 — 无长持资源(每次操作独立租约已随操作释放)</summary>
    public ValueTask DisposeAsync() {
        _gate.DisposeSafe(null);
        return ValueTask.CompletedTask;
    }
}
