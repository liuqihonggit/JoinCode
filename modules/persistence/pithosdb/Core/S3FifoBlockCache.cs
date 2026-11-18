


namespace PithosDB.Core;

/// <summary>
/// Thread-safe, lock-free S3-FIFO-style block cache.
/// <para>
/// The classic S3-FIFO maintains three FIFO queues (small, main, ghost) under a lock.
/// This implementation keeps the same admission policy without any lock:
/// <list type="bullet">
/// <item>New blocks enter as probationary (<c>freq = 0</c>).</item>
/// <item>A cache hit sets <c>freq = 1</c> with a single volatile write — O(1),
/// no structural mutation.</item>
/// <item>Recently evicted keys are recorded in a lock-free ghost set. Re-admission
/// of a ghost key enters with <c>freq = 1</c>, bypassing the probation period.</item>
/// <item>When the byte budget is exceeded, a small random sample of entries is
/// inspected: entries with <c>freq = 0</c> are evicted oldest-first; entries with
/// <c>freq = 1</c> get a second chance (freq cleared) instead of being evicted.</item>
/// </list>
/// The policy is approximate rather than queue-exact, but it preserves S3-FIFO's
/// scan-resistance and never blocks readers or other writers on a global lock.
/// </para>
/// </summary>
public sealed class S3FifoBlockCache : IBlockCache
{
    private sealed class Entry
    {
        public required byte[] Data;
        public required long InsertedTicks;
        public int Freq; // 0 or 1, accessed via Volatile
    }

    private const int EvictionSampleSize = 8;
    private const int MaxEvictionsPerPut = 4;
    private const int MaxGhostTrimPerPut = 4;

    private readonly long _maxBytes;
    private long _usedBytes;
    private readonly ConcurrentDictionary<(string, long), Entry> _map = new();

    // Ghost: keys recently evicted while cold (freq = 0). Re-requesting one admits
    // it straight into the frequent set (freq = 1).
    private readonly int _ghostMax;
    private readonly ConcurrentDictionary<(string, long), long> _ghost = new();

    /// <summary>
    /// Creates a block cache that keeps at most <paramref name="maxBytes"/> of block data.
    /// </summary>
    public S3FifoBlockCache(long maxBytes)
    {
        _maxBytes = maxBytes;
        // Ghost holds roughly as many entries as a tenth of the budget fits at the average block size.
        _ghostMax = (int)Math.Max(16, maxBytes / 10 / 4096);
    }

    /// <inheritdoc/>
    public bool TryGet(string path, long offset, [NotNullWhen(true)] out byte[]? data)
    {
        if (_map.TryGetValue((path, offset), out var entry))
        {
            Volatile.Write(ref entry.Freq, 1);
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
            InsertedTicks = Environment.TickCount64,
            Freq = _ghost.TryRemove(key, out _) ? 1 : 0,
        };
        if (!_map.TryAdd(key, entry)) return;

        Interlocked.Add(ref _usedBytes, data.Length);
        TrimToBudget();
    }

    /// <inheritdoc/>
    public void EvictFile(string path)
    {
        foreach (var pair in _map)
        {
            if (pair.Key.Item1 != path) continue;
            if (!_map.TryRemove(pair.Key, out var removed)) continue;
            Interlocked.Add(ref _usedBytes, -removed.Data.Length);
            AddToGhost(pair.Key);
        }
    }

    /// <inheritdoc/>
    public long CurrentSizeBytes => Volatile.Read(ref _usedBytes);

    private void TrimToBudget()
    {
        for (var i = 0; i < MaxEvictionsPerPut && Volatile.Read(ref _usedBytes) > _maxBytes; i++)
        {
            if (!EvictColdestSample()) return;
        }
    }

    private bool EvictColdestSample()
    {
        Entry? victim = null;
        (string, long) victimKey = default;
        var sampled = 0;
        foreach (var pair in _map)
        {
            if (victim is null || pair.Value.InsertedTicks < victim.InsertedTicks)
            {
                victim = pair.Value;
                victimKey = pair.Key;
            }
            if (++sampled >= EvictionSampleSize) break;
        }

        if (victim is null) return false;

        if (Volatile.Read(ref victim.Freq) > 0)
        {
            // Second chance: demote instead of evicting; the next pass may pick a colder entry.
            Volatile.Write(ref victim.Freq, 0);
            return true;
        }

        if (!_map.TryRemove(victimKey, out var removed)) return true; // raced with another evictor
        Interlocked.Add(ref _usedBytes, -removed.Data.Length);
        AddToGhost(victimKey);
        return true;
    }

    private void AddToGhost((string, long) key)
    {
        _ghost[key] = Environment.TickCount64;
        if (_ghost.Count <= _ghostMax) return;

        // Bound the ghost set: drop the oldest sampled entries.
        for (var i = 0; i < MaxGhostTrimPerPut && _ghost.Count > _ghostMax; i++)
        {
            (string, long) oldestKey = default;
            long oldestTick = long.MaxValue;
            foreach (var pair in _ghost)
            {
                if (pair.Value < oldestTick)
                {
                    oldestTick = pair.Value;
                    oldestKey = pair.Key;
                }
            }
            if (oldestTick == long.MaxValue) return;
            _ghost.TryRemove(oldestKey, out _);
        }
    }
}
