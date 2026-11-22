namespace Infrastructure.Persistence;

/// <summary>
/// PithosDB 租约 — 包装 PithosDb + 进程内占位 + 跨进程原子文件租约，Dispose 时关闭 + 释放租约。
/// <para>磁盘租约：ownsDb=true, crossProcessLock!=null, inProcessKey!=null, IsInMemory=false。</para>
/// <para>降级内存租约：ownsDb=true, crossProcessLock=null, inProcessKey=null, IsInMemory=true（PithosDb.OpenInMemory）。</para>
/// <para>两层互斥：<see cref="InProcessLeaseRegistry"/> 原子占位（同进程防重入）+ <see cref="CrossProcessLock"/> 原子文件租约（跨进程）。</para>
/// <para>释放顺序：先跨进程租约（删除租约文件）再释放进程内占位，与获取顺序相反；释放函数禁止等待（ADR 0093）。</para>
/// </summary>
public sealed class PithosKvStoreLease : IKvStoreLease {
    private readonly PithosDb _db;
    private readonly CrossProcessLock? _crossProcessLock;
    private readonly string? _inProcessKey;
    private readonly bool _ownsDb;
    private int _disposed;

    /// <inheritdoc />
    public bool IsInMemory { get; }

    internal PithosKvStoreLease(PithosDb db, CrossProcessLock? crossProcessLock, string? inProcessKey, bool ownsDb, bool isInMemory) {
        _db = db;
        _crossProcessLock = crossProcessLock;
        _inProcessKey = inProcessKey;
        _ownsDb = ownsDb;
        IsInMemory = isInMemory;
    }

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
    public async IAsyncEnumerable<KvStoreEntry> ScanAsync(
        byte[]? from = null,
        byte[]? to = null,
        [EnumeratorCancellation] CancellationToken ct = default) {
        await foreach (var (key, value) in _db.ScanAsync(from: from, to: to).WithCancellation(ct).ConfigureAwait(false)) {
            yield return new KvStoreEntry(key, value);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>释放 PithosDb + 两层租约。幂等。释放函数禁止等待（ADR 0093）。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsDb) {
            _db.DisposeSafe();
        }
        _crossProcessLock?.DisposeSafe();
        if (_inProcessKey is not null) {
            InProcessLeaseRegistry.Release(_inProcessKey);
        }
    }
}
