using PithosDB.Core.Core;
using PithosDB.Core.Storage;

namespace PithosDB.Core.Compaction;

/// <summary>
/// Implements leveled compaction for the LSM-tree. The engine uses 7 levels
/// whose file-count limits grow by 10× per level (L0 = 10, L1 = 100, …).
/// When a level reaches its limit, all its SSTables are merged into a single
/// SSTable at the next level, deduplicating keys so that newer writes win.
/// </summary>
public sealed class LeveledCompactor
{
    private readonly string _directory;
    private readonly PithosOptions _options;
    private readonly int[] _levelSizeLimits;
    private readonly Dictionary<string, SSTableReader> _readerCache;
    private readonly IBlockCache? _blockCache;
    private readonly Manifest? _manifest;
    private readonly ICompactionFilter? _filter;
    private readonly ReaderWriterLockSlim _dbLock;

    /// <summary>
    /// Creates a compactor for the database at <paramref name="directory"/> using
    /// the provided <paramref name="options"/>.
    /// </summary>
    /// <param name="directory">Database directory where SSTable files are written.</param>
    /// <param name="options">Options governing level count, size limits, and bloom filter FPR.</param>
    /// <param name="readerCache">
    /// Shared SSTableReader cache owned by the database. The compactor evicts
    /// source-file entries before deleting them and registers the merged output
    /// so reads can immediately use the cached reader.
    /// </param>
    /// <param name="blockCache">Optional shared block cache.</param>
    /// <param name="manifest">
    /// Optional manifest. When provided, the manifest is updated after each
    /// compaction step before source files are deleted, ensuring crash-safe recovery.
    /// </param>
    /// <param name="dbLock">
    /// The database's <see cref="ReaderWriterLockSlim"/>. The compactor acquires the
    /// write lock only briefly (to swap level state), keeping reads unblocked during
    /// the expensive I/O phase.
    /// </param>
    public LeveledCompactor(string directory, PithosOptions options, Dictionary<string, SSTableReader> readerCache,
        IBlockCache? blockCache = null, Manifest? manifest = null, ReaderWriterLockSlim? dbLock = null)
    {
        _directory = directory;
        _options = options;
        _readerCache = readerCache;
        _blockCache = blockCache;
        _manifest = manifest;
        _filter = options.CompactionFilter;
        _dbLock = dbLock ?? new ReaderWriterLockSlim();
        _levelSizeLimits = new int[options.LevelCount];
        int size = options.LevelZeroFileCountLimit;
        for (int i = 0; i < options.LevelCount; i++) { _levelSizeLimits[i] = size; size *= options.LevelSizeMultiplier; }
    }

    /// <summary>
    /// Checks each level and triggers a compaction for any level that has reached
    /// its file-count limit. Returns <see langword="true"/> if any compaction was
    /// performed so callers can loop until all levels are within their limits.
    /// Designed to be called from a background thread: expensive I/O runs without
    /// holding <see cref="_dbLock"/>; the write lock is acquired only briefly to
    /// swap level state before and after the I/O phase.
    /// </summary>
    public bool CompactIfNeeded(List<List<string>> levels)
    {
        bool didWork = false;
        for (int level = 0; level < _levelSizeLimits.Length - 1; level++)
        {
            bool needs;
            _dbLock.EnterReadLock();
            try { needs = level < levels.Count && levels[level].Count >= _levelSizeLimits[level]; }
            finally { _dbLock.ExitReadLock(); }

            if (needs) { Compact(levels, level); didWork = true; }
        }
        return didWork;
    }

    /// <summary>
    /// Merges all SSTables at <paramref name="level"/> into a single SSTable at
    /// <c>level + 1</c>, then deletes the source files.
    /// <para>
    /// Locking: the write lock is held only during the two brief state-swap steps
    /// (snapshotting sources and committing the result). All SSTable I/O runs
    /// without any lock so concurrent reads are never blocked by compaction.
    /// </para>
    /// <para>
    /// Crash-safe ordering: the manifest is written after the merged file is
    /// fully written and the in-memory state is committed, but before source files
    /// are deleted. A crash between those steps leaves orphaned source files that
    /// are removed on the next open.
    /// </para>
    /// </summary>
    private void Compact(List<List<string>> levels, int level)
    {
        // ── Phase 1: snapshot sources (brief write lock) ──────────────────────
        // Take at most _levelSizeLimits[level] files at a time. Batching ensures
        // that even when many L0 files have accumulated (because the background
        // thread was behind), each compaction produces exactly one output file
        // rather than merging everything at once into a single file, allowing
        // the destination level to accumulate files and cascade naturally.
        // Source files stay in _levels so concurrent reads continue to find them
        // while I/O is in progress.
        List<string> sources;
        _dbLock.EnterWriteLock();
        try
        {
            while (levels.Count <= level + 1) levels.Add([]);
            sources = [.. levels[level].Take(_levelSizeLimits[level])];
        }
        finally { _dbLock.ExitWriteLock(); }

        if (sources.Count == 0) return;

        // ── Phase 2: merge I/O (no lock) ─────────────────────────────────────
        // Source files are immutable so reads can access them concurrently.
        string outPath = System.IO.Path.Combine(_directory, $"L{level + 1}_{Guid.NewGuid():N}.sst");
        var readers = sources.Select(p => new SSTableReader(p)).ToList();
        try
        {
            var merged = MergeEntries(readers, _options.EnableTtl, _filter);
            SSTableWriter.Write(outPath, merged, _options.BloomFilterFalsePositiveRate, _options.Compression);
        }
        catch
        {
            foreach (var r in readers) r.Dispose();
            if (File.Exists(outPath)) File.Delete(outPath);
            throw;
        }
        finally
        {
            foreach (var r in readers) r.Dispose();
        }

        // ── Phase 3: commit result (brief write lock) ─────────────────────────
        // Atomically: add merged file, remove sources, update reader cache.
        // After this point reads see the merged file and no longer see sources.
        List<SSTableReader> toDispose = [];
        _dbLock.EnterWriteLock();
        try
        {
            levels[level + 1].Add(outPath);
            _readerCache[outPath] = new SSTableReader(outPath, _blockCache);

            foreach (var p in sources)
            {
                levels[level].Remove(p);
                if (_readerCache.TryGetValue(p, out var cached))
                {
                    _readerCache.Remove(p);
                    toDispose.Add(cached); // dispose outside the lock
                }
            }

            _manifest?.Write(levels);
        }
        catch
        {
            // Roll back in-memory state; outPath is an on-disk orphan cleaned up on next open.
            levels[level + 1].Remove(outPath);
            if (_readerCache.TryGetValue(outPath, out var r))
            {
                _readerCache.Remove(outPath);
                r.Dispose();
            }
            throw;
        }
        finally { _dbLock.ExitWriteLock(); }

        // ── Phase 4: cleanup (no lock) ────────────────────────────────────────
        // All in-flight reads that held references to the old readers have already
        // released the read lock before phase 3's write lock was granted, so it is
        // safe to dispose them now.
        foreach (var r in toDispose) r.Dispose();
        foreach (var p in sources)
        {
            _blockCache?.EvictFile(p);
            File.Delete(p);
        }
    }

    /// <summary>
    /// Performs a k-way merge over <paramref name="readers"/> using a
    /// <see cref="PriorityQueue{TElement,TPriority}"/> ordered by
    /// <c>(key asc, readerIndex desc)</c>. For duplicate keys across readers,
    /// the entry from the highest-indexed reader (i.e. the newest SSTable) is
    /// emitted and all older copies are discarded.
    /// </summary>
    private static IEnumerable<KeyValuePair<byte[], byte[]?>> MergeEntries(
        List<SSTableReader> readers, bool enableTtl, ICompactionFilter? filter)
    {
        var comparer = ByteArrayComparer.Instance;
        var enumerators = readers.Select(r => r.ReadAllEntries().GetEnumerator()).ToList();

        // Priority: key asc, then negated readerIndex asc (so higher index = newer = dequeued first on ties)
        var pq = new PriorityQueue<(byte[] key, byte[]? value, int readerIdx), (byte[] key, int negIdx)>(
            Comparer<(byte[] key, int negIdx)>.Create((a, b) =>
            {
                int c = comparer.Compare(a.key, b.key);
                return c != 0 ? c : a.negIdx.CompareTo(b.negIdx);
            }));

        for (int i = 0; i < enumerators.Count; i++)
        {
            if (enumerators[i].MoveNext())
            {
                var kv = enumerators[i].Current;
                pq.Enqueue((kv.Key, kv.Value, i), (kv.Key, -i));
            }
        }

        byte[]? lastKey = null;
        while (pq.Count > 0)
        {
            var (key, value, idx) = pq.Dequeue();

            if (lastKey is null || comparer.Compare(lastKey, key) != 0)
            {
                lastKey = key;

                if (value is null)
                {
                    // Tombstone — always pass through so it shadows older copies.
                    yield return new KeyValuePair<byte[], byte[]?>(key, null);
                }
                else
                {
                    // Decode for TTL check and filter; re-emit original encoded bytes
                    // so the TTL header is preserved for future reads and compactions.
                    byte[] userValue = value;
                    bool keep = true;

                    if (enableTtl)
                    {
                        var (decoded, dropped) = ValueCodec.DecodeForCompaction(value);
                        if (dropped) keep = false;
                        else userValue = decoded!;
                    }

                    if (keep && (filter is null || filter.ShouldKeep(key, userValue)))
                        yield return new KeyValuePair<byte[], byte[]?>(key, value);
                }
            }

            if (enumerators[idx].MoveNext())
            {
                var kv = enumerators[idx].Current;
                pq.Enqueue((kv.Key, kv.Value, idx), (kv.Key, -idx));
            }
        }

        foreach (var e in enumerators) e.Dispose();
    }
}
