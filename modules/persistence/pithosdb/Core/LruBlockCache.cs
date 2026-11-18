using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace PithosDB.Core.Core;

/// <summary>
/// Thread-safe, lock-free block cache shared across all <see cref="Storage.SSTableReader"/>
/// instances. Caches raw block bytes keyed by (file path, block offset) so hot blocks
/// are served from memory rather than re-read from disk on every lookup.
/// <para>
/// Fully lock-free: entries live in a <see cref="ConcurrentDictionary{TKey,TValue}"/> and
/// recency is tracked with an atomic tick, so a cache hit is one dictionary read plus one
/// volatile write. Eviction is sampled LRU — when the byte budget is exceeded, a small
/// random sample of entries is inspected and the least-recently-used sample is removed.
/// The policy is approximate (theoretical worst case slightly below exact LRU) but needs
/// no global lock, so readers and evictors never block each other.
/// </para>
/// </summary>
public sealed class LruBlockCache : IBlockCache
{
    private sealed class Entry
    {
        public required byte[] Data;
        public long LastAccessTicks;
    }

    private const int EvictionSampleSize = 8;
    private const int MaxEvictionsPerPut = 4;

    private readonly long _maxBytes;
    private long _usedBytes;
    private readonly ConcurrentDictionary<(string, long), Entry> _map = new();

    /// <summary>
    /// Creates a block cache that keeps at most <paramref name="maxBytes"/> of block data.
    /// </summary>
    public LruBlockCache(long maxBytes) => _maxBytes = maxBytes;

    /// <inheritdoc/>
    public bool TryGet(string path, long offset, [NotNullWhen(true)] out byte[]? data)
    {
        if (_map.TryGetValue((path, offset), out var entry))
        {
            Volatile.Write(ref entry.LastAccessTicks, Environment.TickCount64);
            data = entry.Data;
            return true;
        }
        data = null;
        return false;
    }

    /// <inheritdoc/>
    public void Put(string path, long offset, byte[] data)
    {
        var key = (path, offset);
        if (_map.ContainsKey(key)) return;

        var entry = new Entry
        {
            Data = data,
            LastAccessTicks = Environment.TickCount64,
        };
        if (!_map.TryAdd(key, entry)) return;

        Interlocked.Add(ref _usedBytes, data.Length);
        TrimToBudget();
    }

    /// <summary>
    /// Evicts all cached blocks belonging to <paramref name="path"/>. Call this
    /// before deleting the underlying SSTable file.
    /// </summary>
    public void EvictFile(string path)
    {
        foreach (var pair in _map)
        {
            if (pair.Key.Item1 != path) continue;
            if (_map.TryRemove(pair.Key, out var removed))
                Interlocked.Add(ref _usedBytes, -removed.Data.Length);
        }
    }

    /// <inheritdoc/>
    public long CurrentSizeBytes => Volatile.Read(ref _usedBytes);

    private void TrimToBudget()
    {
        for (var i = 0; i < MaxEvictionsPerPut && Volatile.Read(ref _usedBytes) > _maxBytes; i++)
        {
            if (!TryEvictOldest()) return;
        }
    }

    private bool TryEvictOldest()
    {
        Entry? victim = null;
        (string, long) victimKey = default;
        var sampled = 0;
        foreach (var pair in _map)
        {
            if (victim is null || pair.Value.LastAccessTicks < victim.LastAccessTicks)
            {
                victim = pair.Value;
                victimKey = pair.Key;
            }
            if (++sampled >= EvictionSampleSize) break;
        }

        if (victim is null) return false;
        if (!_map.TryRemove(victimKey, out var removed)) return true; // raced with another evictor, retry sampling
        Interlocked.Add(ref _usedBytes, -removed.Data.Length);
        return true;
    }
}
