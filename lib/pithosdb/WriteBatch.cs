using PithosDB.Core.Core;

namespace PithosDB.Core;

/// <summary>
/// An atomic batch of Put and Delete operations. When applied via
/// <see cref="PithosDb.Write"/>, all operations are written to the WAL in a
/// single CRC-guarded record: either every operation survives a crash or none do.
/// </summary>
public sealed class WriteBatch
{
    internal readonly List<(WalEntryType Type, byte[] Key, byte[]? Value, TimeSpan? Ttl)> Operations = [];

    /// <summary>Adds a Put operation to the batch.</summary>
    public WriteBatch Put(byte[] key, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        Operations.Add((WalEntryType.Put, key, value, null));
        return this;
    }

    /// <summary>
    /// Adds a Put operation with a TTL to the batch.
    /// Requires <see cref="PithosOptions.EnableTtl"/> to be <see langword="true"/>
    /// on the database that applies this batch.
    /// </summary>
    public WriteBatch Put(byte[] key, byte[] value, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(value);
        Operations.Add((WalEntryType.Put, key, value, ttl));
        return this;
    }

    /// <summary>Adds a Delete operation to the batch.</summary>
    public WriteBatch Delete(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Operations.Add((WalEntryType.Delete, key, null, null));
        return this;
    }
}
