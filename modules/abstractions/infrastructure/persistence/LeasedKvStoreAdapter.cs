namespace Infrastructure.Persistence;

/// <summary>
/// 短租约 KV 存储适配器 — 每次 KV 操作独立租约(Acquire→执行→Dispose),遵守 ADR 0139 短租约协议。
/// <para>用途: 长生命周期 DI 单例(如 gh 缓存 GitHubToolHandlers/GhCacheService)消费 IKvStore 时,
/// 不持有进程级锁(长驻 mcp server 会饿死所有 CLI 进程→降级内存→缓存永远 miss),
/// 每次操作只占用几毫秒竞争窗口,CLI 与常驻进程可交替获得磁盘租约。</para>
/// <para>进程内串行化: <see cref="AsyncGate"/> 任务链门闩 — 操作排队而非并发竞争,
/// 防止同进程并发操作全部因租约被占而降级内存导致写入丢失。</para>
/// <para>TTL 扩展(KvStoreTtlExtensions)直接可用 — 适配器实现 IKvStore 四个核心方法,扩展方法自动转发。</para>
/// </summary>
public sealed class LeasedKvStoreAdapter : IKvStore {
    private readonly IKvStoreLeaseFactory _factory;
    private readonly AsyncGate _gate = new();

    /// <summary>创建短租约适配器 — 每次操作从工厂 Acquire 短租约并转发</summary>
    /// <param name="factory">KV 存储租约工厂(磁盘/内存由工厂决定)</param>
    public LeasedKvStoreAdapter(IKvStoreLeaseFactory factory) {
        ArgumentNullException.ThrowIfNull(factory);
        _factory = factory;
    }

    /// <inheritdoc />
    public async ValueTask PutAsync(byte[] key, byte[] value, CancellationToken ct = default) {
        using var serial = await _gate.AcquireAsync(ct).ConfigureAwait(false);
        using var lease = await _factory.AcquireAsync().ConfigureAwait(false);
        await lease.PutAsync(key, value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken ct = default) {
        using var serial = await _gate.AcquireAsync(ct).ConfigureAwait(false);
        using var lease = await _factory.AcquireAsync().ConfigureAwait(false);
        return await lease.GetAsync(key, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DeleteAsync(byte[] key, CancellationToken ct = default) {
        using var serial = await _gate.AcquireAsync(ct).ConfigureAwait(false);
        using var lease = await _factory.AcquireAsync().ConfigureAwait(false);
        await lease.DeleteAsync(key, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <para>范围扫描在枚举期间持有租约(yield 需要底层 db 存活),枚举结束/中断释放 — using 保证。</para>
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
        byte[]? from = null, byte[]? to = null, [EnumeratorCancellation] CancellationToken ct = default) {
        using var serial = await _gate.AcquireAsync(ct).ConfigureAwait(false);
        using var lease = await _factory.AcquireAsync().ConfigureAwait(false);
        await foreach (var kv in lease.ScanAsync(from, to, ct).ConfigureAwait(false)) {
            yield return kv;
        }
    }

    /// <summary>无长持资源 — 每次操作独立租约已随操作释放；AsyncGate 无释放动作。</summary>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
