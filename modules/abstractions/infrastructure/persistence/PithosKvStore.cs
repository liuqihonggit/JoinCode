namespace Infrastructure.Persistence;

/// <summary>
/// PithosDB LSM-Tree 键值存储实现 — 通用持久化层。
/// <para>基于 PithosDB 1.6.0: WAL + MemTable + SSTable + Leveled Compaction + Bloom Filter + Block Cache + LZ4 压缩。</para>
/// <para>所有操作线程安全(ReaderWriterLockSlim: 并发读 + 串行写)。</para>
/// <para>使用方式: new PithosKvStore("path/to/data-dir") 或 PithosKvStore.OpenInMemory()</para>
/// </summary>
public sealed class PithosKvStore : IKvStore {
    private readonly PithosDb _db;
    private readonly bool _ownsDb;

    /// <summary>
    /// 创建磁盘持久化 KV 存储 — 数据目录不存在时自动创建。
    /// </summary>
    /// <param name="dataDirectory">数据目录路径</param>
    /// <param name="options">可选配置(null=默认配置)</param>
    public PithosKvStore(string dataDirectory, PithosOptions? options = null) {
        _db = options is not null ? PithosDb.Open(dataDirectory, options) : PithosDb.Open(dataDirectory);
        _ownsDb = true;
    }

    /// <summary>
    /// 从已有 PithosDb 实例包装 — 不接管 Dispose 责任。
    /// </summary>
    public PithosKvStore(PithosDb db) {
        _db = db;
        _ownsDb = false;
    }

    /// <summary>
    /// 创建纯内存 KV 存储 — 无磁盘 IO, 用于测试或临时数据。
    /// </summary>
    public static PithosKvStore OpenInMemory() => new(PithosDb.OpenInMemory());

    /// <inheritdoc />
    public ValueTask PutAsync(byte[] key, byte[] value, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return new(_db.PutAsync(key, value));
    }

    /// <inheritdoc />
    public async ValueTask<byte[]?> GetAsync(byte[] key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return await _db.GetAsync(key).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public ValueTask DeleteAsync(byte[] key, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return new(_db.DeleteAsync(key));
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
        byte[]? from = null,
        byte[]? to = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default) {
        await foreach (var (key, value) in _db.ScanAsync(from: from, to: to).WithCancellation(ct).ConfigureAwait(false)) {
            yield return (key, value);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() {
        if (_ownsDb) {
            _db.Dispose();
        }
        return ValueTask.CompletedTask;
    }
}
