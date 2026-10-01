using PithosDB.Core.Core;
using PithosDB.Core.Storage;

namespace PithosDB.Core;

/// <summary>
/// A point-in-time, read-only view of a <see cref="PithosDb"/> database created by
/// <see cref="PithosDb.GetSnapshot"/>. The snapshot reflects the exact state at the
/// moment it was taken and remains consistent regardless of subsequent writes or
/// compactions. Dispose when finished to release the underlying file handles.
/// </summary>
public sealed class Snapshot : IDisposable
{
    private readonly SortedDictionary<byte[], byte[]?> _mem;
    private readonly List<List<string>> _levelPaths;
    private readonly Dictionary<string, SSTableReader> _readers;
    private readonly PithosOptions _options;
    private bool _disposed;

    internal Snapshot(
        SortedDictionary<byte[], byte[]?> mem,
        List<List<string>> levelPaths,
        Dictionary<string, SSTableReader> readers,
        PithosOptions options)
    {
        _mem = mem;
        _levelPaths = levelPaths;
        _readers = readers;
        _options = options;
    }

    /// <summary>
    /// Looks up <paramref name="key"/> in this snapshot. Returns <see langword="false"/>
    /// if the key did not exist at snapshot time, was deleted, had expired, or is excluded
    /// by the compaction filter.
    /// </summary>
    public bool TryGet(byte[] key, out byte[]? value)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_mem.TryGetValue(key, out var raw))
        {
            if (raw is null) { value = null; return false; } // tombstone
            return DecodeAndFilter(key, raw, out value);
        }

        foreach (var level in _levelPaths)
        foreach (var path in Enumerable.Reverse(level))
        {
            if (_readers.TryGetValue(path, out var reader) && reader.TryGet(key, out raw))
            {
                if (raw is null) { value = null; return false; } // tombstone
                return DecodeAndFilter(key, raw, out value);
            }
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Returns all live key-value pairs visible at snapshot time within the inclusive
    /// range [<paramref name="from"/>, <paramref name="to"/>], in sorted order. Omit
    /// either bound for an open-ended scan; omit both for a full scan.
    /// </summary>
    public IEnumerable<(byte[] key, byte[] value)> Scan(byte[]? from = null, byte[]? to = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return CollectScan(from, to);
    }

    private bool DecodeAndFilter(byte[] key, byte[] raw, out byte[]? value)
    {
        if (_options.EnableTtl)
        {
            value = ValueCodec.Decode(raw);
            if (value is null) return false;
        }
        else
        {
            value = raw;
        }

        if (_options.CompactionFilter?.ShouldKeep(key, value) == false)
        {
            value = null;
            return false;
        }
        return true;
    }

    private List<(byte[] key, byte[] value)> CollectScan(byte[]? from, byte[]? to)
    {
        var comparer = ByteArrayComparer.Instance;

        var sources = new List<IEnumerable<KeyValuePair<byte[], byte[]?>>> { _mem };
        foreach (var level in _levelPaths)
            foreach (var path in Enumerable.Reverse(level))
                if (_readers.TryGetValue(path, out var reader))
                    sources.Add(reader.ReadAllEntries());

        var pq = new PriorityQueue<(byte[] key, byte[]? value, int src), (byte[], int)>(
            Comparer<(byte[], int)>.Create((a, b) =>
            {
                int c = comparer.Compare(a.Item1, b.Item1);
                return c != 0 ? c : a.Item2.CompareTo(b.Item2);
            }));

        var enumerators = sources.Select(s => s.GetEnumerator()).ToList();
        for (int i = 0; i < enumerators.Count; i++)
        {
            if (enumerators[i].MoveNext())
            {
                var kv = enumerators[i].Current;
                pq.Enqueue((kv.Key, kv.Value, i), (kv.Key, i));
            }
        }

        var results = new List<(byte[] key, byte[] value)>();
        byte[]? lastKey = null;

        while (pq.Count > 0)
        {
            var (key, value, idx) = pq.Dequeue();
            bool isDuplicate = lastKey is not null && comparer.Compare(lastKey, key) == 0;
            lastKey = key;

            if (enumerators[idx].MoveNext())
            {
                var kv = enumerators[idx].Current;
                pq.Enqueue((kv.Key, kv.Value, idx), (kv.Key, idx));
            }

            if (isDuplicate) continue;
            if (value is null) continue; // tombstone

            byte[] userValue;
            if (_options.EnableTtl)
            {
                var decoded = ValueCodec.Decode(value);
                if (decoded is null) continue;
                userValue = decoded;
            }
            else
            {
                userValue = value;
            }

            if (_options.CompactionFilter?.ShouldKeep(key, userValue) == false) continue;
            if (from is not null && comparer.Compare(key, from) < 0) continue;
            if (to   is not null && comparer.Compare(key, to)   > 0) break;

            results.Add((key, userValue));
        }

        foreach (var e in enumerators) e.Dispose();
        return results;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var reader in _readers.Values)
            reader.Dispose();
        _readers.Clear();
    }
}
