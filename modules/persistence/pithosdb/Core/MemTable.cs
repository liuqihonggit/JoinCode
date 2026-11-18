namespace PithosDB.Core;

/// <summary>
/// In-memory write buffer for the LSM-tree. Entries are held in a
/// <see cref="ConcurrentDictionary{TKey,TValue}"/> keyed by byte-lexicographic
/// equality, so readers may snapshot-enumerate the buffer while the single
/// writer thread keeps inserting. Sorted key order is materialised on demand by
/// <see cref="GetSortedEntries"/> for flush and scan paths.
/// Deleted keys are represented as <see langword="null"/> tombstone values.
/// </summary>
public class MemTable
{
    private readonly ConcurrentDictionary<byte[], byte[]?> _data =
        new(ByteArrayComparer.Instance);

    private long _sizeBytes;

    /// <summary>Approximate in-memory size of all keys and values in bytes.</summary>
    public long SizeBytes => Volatile.Read(ref _sizeBytes);

    /// <summary>Number of entries including tombstones.</summary>
    public int Count => _data.Count;

    /// <summary>
    /// Inserts or updates <paramref name="key"/> with <paramref name="value"/>.
    /// <see cref="SizeBytes"/> is adjusted to reflect the new entry size.
    /// </summary>
    public void Put(byte[] key, byte[] value)
    {
        if (_data.TryGetValue(key, out var existing))
            Interlocked.Add(ref _sizeBytes, value.Length - (existing?.Length ?? 0));
        else
            Interlocked.Add(ref _sizeBytes, key.Length + value.Length);

        _data[key] = value;
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
            Interlocked.Add(ref _sizeBytes, -(key.Length + (existing?.Length ?? 0)));

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
    /// Returns a materialised copy of all entries in byte-lexicographic key order,
    /// including tombstones. Safe to enumerate while the writer keeps inserting —
    /// the snapshot reflects the moment the call started.
    /// <para>
    /// The entries are copied with the dictionary's own snapshot enumerator (LINQ's
    /// <c>ICollection.CopyTo</c> path pre-sizes the array from a racy <see cref="Count"/>
    /// and can throw when the writer grows the table concurrently).
    /// </para>
    /// </summary>
    public IEnumerable<KeyValuePair<byte[], byte[]?>> GetSortedEntries()
    {
        var entries = new List<KeyValuePair<byte[], byte[]?>>(_data.Count);
        foreach (var entry in _data)
            entries.Add(entry);
        entries.Sort(static (a, b) => ByteArrayComparer.Instance.Compare(a.Key, b.Key));
        return entries;
    }
}
