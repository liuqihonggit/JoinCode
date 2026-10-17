namespace Infrastructure.Persistence;

/// <summary>
/// PithosDB 租约 — 包装 PithosDb + 两锁联合（SemaphoreSlim 防重入 + CrossProcessLock 跨进程），Dispose 时 Close + 释放锁。
/// <para>磁盘租约：ownsDb=true, crossProcessLock!=null, semaphore!=null, IsInMemory=false。</para>
/// <para>降级内存租约：ownsDb=true, crossProcessLock=null, semaphore=null, IsInMemory=true（PithosDb.OpenInMemory）。</para>
/// <para>两锁联合（复用 NamedMutexMailboxLock 模式）：SemaphoreSlim(1,1) 进程内防重入 + CrossProcessLock 跨进程互斥。</para>
/// <para>释放顺序：先 CrossProcessLock（跨进程）再 SemaphoreSlim（进程内），与获取顺序相反。</para>
/// </summary>
public sealed class PithosKvStoreLease : IKvStoreLease {
    private readonly PithosDb _db;
    private readonly CrossProcessLock? _crossProcessLock;
    private readonly SemaphoreSlim? _semaphore;
    private readonly bool _ownsDb;
    private int _disposed;

    /// <inheritdoc />
    public bool IsInMemory { get; }

    internal PithosKvStoreLease(PithosDb db, CrossProcessLock? crossProcessLock, SemaphoreSlim? semaphore, bool ownsDb, bool isInMemory) {
        _db = db;
        _crossProcessLock = crossProcessLock;
        _semaphore = semaphore;
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

    /// <summary>释放 PithosDb + 两锁。幂等。释放函数禁止超时（ADR 0093）。</summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        if (_ownsDb) {
            _db.DisposeSafe();
        }
        _crossProcessLock?.DisposeSafe();
        if (_semaphore is not null) {
            try { _semaphore.Release(); }
            catch (ObjectDisposedException ex) { Console.Error.WriteLine($"[PithosKvStoreLease] Semaphore released after dispose: {ex.Message}"); }
            catch (SemaphoreFullException ex) { Console.Error.WriteLine($"[PithosKvStoreLease] Semaphore full: {ex.Message}"); }
        }
    }
}
