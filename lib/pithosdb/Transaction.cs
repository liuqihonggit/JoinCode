using PithosDB.Core.Core;

namespace PithosDB.Core;

/// <summary>
/// A read-write transaction over a <see cref="PithosDb"/> instance. Reads are served
/// from a point-in-time <see cref="Snapshot"/> taken at transaction start; writes are
/// buffered locally and applied atomically on <see cref="Commit"/>.
/// <para>
/// Concurrency model: <em>optimistic</em>. No locks are held between
/// <see cref="PithosDb.BeginTransaction"/> and <see cref="Commit"/>. At commit time the
/// engine acquires the write lock once, validates that every key in the read set is
/// unchanged since the snapshot was taken, then atomically applies the write buffer.
/// If any read key was modified by a concurrent writer, <see cref="Commit"/> throws
/// <see cref="TransactionConflictException"/> and the transaction is finalized; begin a
/// new transaction to retry.
/// </para>
/// <para>
/// <see cref="TryGet"/> exhibits <em>read-your-own-writes</em>: values buffered by
/// <see cref="Put"/> or <see cref="Delete"/> within this transaction are visible to
/// subsequent <see cref="TryGet"/> calls in the same transaction.
/// </para>
/// </summary>
public sealed class Transaction : IDisposable
{
    private readonly PithosDb _db;
    private readonly Snapshot _snapshot;
    private readonly Dictionary<byte[], (bool isDelete, byte[]? value)> _writes;
    private readonly HashSet<byte[]> _readSet;
    private bool _disposed;

    internal Transaction(PithosDb db, Snapshot snapshot)
    {
        _db = db;
        _snapshot = snapshot;
        _writes = new Dictionary<byte[], (bool, byte[]?)>(ByteArrayComparer.Instance);
        _readSet = new HashSet<byte[]>(ByteArrayComparer.Instance);
    }

    /// <summary>
    /// Looks up <paramref name="key"/>. If the key has been written or deleted within this
    /// transaction the local buffer is returned (read-your-own-writes). Otherwise the value
    /// from the snapshot taken at transaction start is returned and the key is added to the
    /// read set for conflict detection at commit time.
    /// </summary>
    public bool TryGet(byte[] key, out byte[]? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_writes.TryGetValue(key, out var buffered))
        {
            if (buffered.isDelete) { value = null; return false; }
            value = buffered.value;
            return true;
        }

        _readSet.Add(key);
        return _snapshot.TryGet(key, out value);
    }

    /// <summary>
    /// Buffers an insert or update of <paramref name="key"/> with <paramref name="value"/>.
    /// The write is not visible to other readers until <see cref="Commit"/> succeeds.
    /// </summary>
    public void Put(byte[] key, byte[] value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _writes[key] = (false, value);
    }

    /// <summary>
    /// Buffers a deletion of <paramref name="key"/>.
    /// The tombstone is not written until <see cref="Commit"/> succeeds.
    /// </summary>
    public void Delete(byte[] key)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _writes[key] = (true, null);
    }

    /// <summary>
    /// Validates the read set and atomically applies the buffered writes.
    /// Finalizes the transaction regardless of outcome — call <see cref="PithosDb.BeginTransaction"/>
    /// again to retry after a conflict.
    /// </summary>
    /// <exception cref="TransactionConflictException">
    /// A key in the read set was modified by a concurrent writer before this commit completed.
    /// </exception>
    /// <exception cref="ObjectDisposedException">
    /// The transaction has already been committed, rolled back, or disposed.
    /// </exception>
    public void Commit()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _disposed = true;

        var batch = new WriteBatch();
        foreach (var (key, (isDelete, value)) in _writes)
        {
            if (isDelete) batch.Delete(key);
            else batch.Put(key, value!);
        }

        bool success;
        try { success = _db.TryCommitTransaction(_snapshot, _readSet, batch); }
        finally { _snapshot.Dispose(); }

        if (!success) throw new TransactionConflictException();
    }

    /// <summary>
    /// Asynchronously validates and applies the buffered writes.
    /// </summary>
    public Task CommitAsync(CancellationToken cancellationToken = default)
        => Task.Run(Commit, cancellationToken);

    /// <summary>
    /// Discards all buffered writes and finalizes the transaction.
    /// Calling <see cref="Rollback"/> on an already-finalized transaction is a no-op.
    /// </summary>
    public void Rollback()
    {
        if (_disposed) return;
        _disposed = true;
        _snapshot.Dispose();
    }

    /// <inheritdoc cref="Rollback"/>
    public void Dispose() => Rollback();
}
