namespace PithosDB.Core.Core;

/// <summary>
/// In-memory write buffer for the LSM-tree. Entries are stored in a
/// <see cref="SortedDictionary{TKey,TValue}"/> keyed by byte-lexicographic order,
/// so <see cref="GetSortedEntries"/> can be flushed directly to an SSTable.
/// Deleted keys are represented as <see langword="null"/> tombstone values.
/// </summary>
public class MemTable
{
    private readonly SortedDictionary<byte[], byte[]?> _data =
        new(ByteArrayComparer.Instance);

    private long _sizeBytes;

    /// <summary>Approximate in-memory size of all keys and values in bytes.</summary>
    public long SizeBytes => _sizeBytes;

    /// <summary>Number of entries including tombstones.</summary>
    public int Count => _data.Count;

    /// <summary>
    /// Inserts or updates <paramref name="key"/> with <paramref name="value"/>.
    /// <see cref="SizeBytes"/> is adjusted to reflect the new entry size.
    /// </summary>
    public void Put(byte[] key, byte[] value)
    {
        if (_data.TryGetValue(key, out var existing))
            _sizeBytes -= existing?.Length ?? 0;
        else
            _sizeBytes += key.Length;

        _data[key] = value;
        _sizeBytes += value.Length;
    }

    /// <summary>
    /// Marks <paramref name="key"/> as deleted by storing a <see langword="null"/>
    /// tombstone. <see cref="TryGet"/> will still return <see langword="true"/> for
    /// this key (with a <see langword="null"/> value) so that the tombstone shadows
    /// older values in lower SSTable levels.
    /// </summary>
    public void Delete(byte[] key)
    {
        if (_data.TryGetValue(key, out var existing))
            _sizeBytes -= key.Length + (existing?.Length ?? 0);

        _data[key] = null; // tombstone
    }

    /// <summary>
    /// Looks up <paramref name="key"/>. Returns <see langword="true"/> if the key is
    /// present, including when it holds a tombstone (<paramref name="value"/> is
    /// <see langword="null"/>). Returns <see langword="false"/> only when the key has
    /// never been written to this MemTable.
    /// </summary>
    public bool TryGet(byte[] key, out byte[]? value) =>
        _data.TryGetValue(key, out value);

    /// <summary>
    /// Returns all entries in byte-lexicographic key order, including tombstones.
    /// Used when flushing to an SSTable.
    /// </summary>
    public IEnumerable<KeyValuePair<byte[], byte[]?>> GetSortedEntries() => _data;

    /// <summary>Removes all entries and resets <see cref="SizeBytes"/> to zero.</summary>
    public void Clear()
    {
        _data.Clear();
        _sizeBytes = 0;
    }
}
