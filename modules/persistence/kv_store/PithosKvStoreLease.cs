namespace JoinCode.Persistence.KvStore;

/// <summary>
/// PithosDB 租约 — 包装 PithosDb + 两层租约，Dispose 时关闭 + 释放租约。
/// <para>磁盘租约：ownsDb=true, mailboxLock!=null, IsInMemory=false。</para>
/// <para>降级内存租约：ownsDb=true, mailboxLock=null, IsInMemory=true（PithosDb.OpenInMemory）。</para>
/// <para>两层互斥由 <see cref="NamedMutexMailboxLock"/> 封装：进程内原子占位（同进程防重入）+ 跨进程原子文件租约。</para>
/// <para>释放顺序：先关闭 PithosDb，再释放 <see cref="NamedMutexMailboxLock"/>（内部先删租约文件再释放进程内占位）；释放函数禁止等待（ADR 0093）。</para>
/// </summary>
public sealed class PithosKvStoreLease : IKvStoreLease {
    private readonly PithosDb _db;
    private readonly NamedMutexMailboxLock? _mailboxLock;
    private readonly bool _ownsDb;
    private int _disposed;

    /// <inheritdoc />
    public bool IsInMemory { get; }

    internal PithosKvStoreLease(PithosDb db, NamedMutexMailboxLock? mailboxLock, bool ownsDb, bool isInMemory) {
        _db = db;
        _mailboxLock = mailboxLock;
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

    /// <summary>释放两层租约 + PithosDb。幂等。释放函数禁止等待（ADR 0093）。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsDb) {
            _db.DisposeSafe();
        }
        try {
            _mailboxLock?.Release();
        } catch (Exception ex) {
            System.Diagnostics.Trace.WriteLine($"[PithosKvStoreLease] release mailbox lock failed: {ex.Message}");
        }
    }
}
