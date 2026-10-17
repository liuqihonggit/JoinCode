namespace Infrastructure.Persistence;

/// <summary>
/// PithosDB 租约 — 包装 PithosDb + 跨进程 Mutex，Dispose 时 Close + 释放锁。
/// <para>磁盘租约：ownsDb=true, mutex!=null, IsInMemory=false。</para>
/// <para>降级内存租约：ownsDb=true, mutex=null, IsInMemory=true（PithosDb.OpenInMemory）。</para>
/// </summary>
public sealed class PithosKvStoreLease : IKvStoreLease {
    private readonly PithosDb _db;
    private readonly Mutex? _mutex;
    private readonly bool _ownsDb;
    private int _disposed;

    /// <inheritdoc />
    public bool IsInMemory { get; }

    internal PithosKvStoreLease(PithosDb db, Mutex? mutex, bool ownsDb, bool isInMemory) {
        _db = db;
        _mutex = mutex;
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
    public async IAsyncEnumerable<(byte[] Key, byte[] Value)> ScanAsync(
        byte[]? from = null,
        byte[]? to = null,
        [EnumeratorCancellation] CancellationToken ct = default) {
        await foreach (var (key, value) in _db.ScanAsync(from: from, to: to).WithCancellation(ct).ConfigureAwait(false)) {
            yield return (key, value);
        }
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() {
        Dispose();
        return ValueTask.CompletedTask;
    }

    /// <summary>释放 PithosDb + Mutex。幂等。释放函数禁止超时（ADR 0093）。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsDb) {
            _db.DisposeSafe();
        }
        if (_mutex is not null) {
            try { _mutex.ReleaseMutex(); }
            catch (Exception ex) { Console.Error.WriteLine($"[PithosKvStoreLease] ReleaseMutex failed: {ex.Message}"); }
            _mutex.DisposeSafe();
        }
    }
}
