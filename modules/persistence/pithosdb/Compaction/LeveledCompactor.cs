


namespace PithosDB.Core.Compaction;

/// <summary>
/// Implements leveled compaction for the LSM-tree. The engine uses 7 levels
/// whose file-count limits grow by 10× per level (L0 = 10, L1 = 100, …).
/// When a level reaches its limit, a batch of its SSTables is merged into a single
/// SSTable at the next level, deduplicating keys so that newer writes win.
/// <para>
/// Lock-free coordination: the compactor never blocks readers. It snapshots the
/// published level listing, performs the merge I/O against immutable SSTable files,
/// then asks the database to commit the new state through the writer mailbox (which
/// publishes a new immutable view and rewrites the manifest). Retired readers and
/// source files are reclaimed only after the database reports that all in-flight
/// reads have drained.
/// </para>
/// </summary>
internal sealed class LeveledCompactor
{
    private readonly string _directory;
    private readonly PithosOptions _options;
    private readonly int[] _levelSizeLimits;
    private readonly PithosDb _db;
    private readonly IBlockCache? _blockCache;
    private readonly ICompactionFilter? _filter;

    /// <summary>
    /// Creates a compactor for the database at <paramref name="directory"/> using
    /// the provided <paramref name="options"/>.
    /// </summary>
    /// <param name="directory">Database directory where SSTable files are written.</param>
    /// <param name="options">Options governing level count, size limits, and bloom filter FPR.</param>
    /// <param name="db">
    /// Owning database. Provides the published level listing and commits the result
    /// (view publication + manifest write) through its single-writer mailbox.
    /// </param>
    /// <param name="blockCache">Optional shared block cache, evicted per retired source file.</param>
    public LeveledCompactor(string directory, PithosOptions options, PithosDb db, IBlockCache? blockCache = null)
    {
        _directory = directory;
        _options = options;
        _db = db;
        _blockCache = blockCache;
        _filter = options.CompactionFilter;
        _levelSizeLimits = new int[options.LevelCount];
        int size = options.LevelZeroFileCountLimit;
        for (int i = 0; i < options.LevelCount; i++) { _levelSizeLimits[i] = size; size *= options.LevelSizeMultiplier; }
    }

    /// <summary>
    /// Checks each level and triggers a compaction for any level that has reached
    /// its file-count limit. Returns <see langword="true"/> if any compaction was
    /// performed so callers can loop until all levels are within their limits.
    /// Designed to be called from a background thread: the level listing is read
    /// lock-free and all merge I/O runs without blocking readers.
    /// </summary>
    public bool CompactIfNeeded()
    {
        bool didWork = false;
        for (int level = 0; level < _levelSizeLimits.Length - 1; level++)
        {
            var levels = _db.CurrentLevels;
            if (level < levels.Count && levels[level].Count >= _levelSizeLimits[level])
                didWork |= Compact(level);
        }
        return didWork;
    }

    /// <summary>
    /// Merges a batch of SSTables at <paramref name="level"/> into a single SSTable at
    /// <c>level + 1</c>, then deletes the source files.
    /// <para>
    /// Concurrency: the source listing is captured from the published view. Source
    /// files stay reachable through the current view while the merge runs, so reads
    /// continue to see them. The commit is a single writer-mailbox operation, and the
    /// merged file is fully written before the commit makes it visible.
    /// </para>
    /// <para>
    /// Crash-safe ordering: the manifest is rewritten by the commit before source files
    /// are deleted. A crash between those steps leaves orphaned source files that are
    /// removed on the next open.
    /// </para>
    /// </summary>
    private bool Compact(int level)
    {
        // Take at most _levelSizeLimits[level] files at a time. Batching ensures
        // that even when many L0 files have accumulated (because the background
        // thread was behind), each compaction produces exactly one output file
        // rather than merging everything at once into a single file, allowing
        // the destination level to accumulate files and cascade naturally.
        var levels = _db.CurrentLevels;
        if (level >= levels.Count) return false;

        var sources = levels[level].Take(_levelSizeLimits[level]).ToList();
        if (sources.Count == 0) return false;

        // ── Merge I/O (no state lock, readers keep using the current view) ────
        string outPath = Path.Combine(_directory, $"L{level + 1}_{Guid.NewGuid():N}.sst");
        SSTableReader newReader;
        try
        {
            var readers = sources.Select(p => SSTableReader.Open(p)).ToList();
            try
            {
                var merged = MergeEntries(readers, _options.EnableTtl, _filter);
                SSTableWriter.Write(outPath, merged, _options.BloomFilterFalsePositiveRate, _options.Compression);
            }
            finally
            {
                foreach (var r in readers) r.Dispose();
            }
            newReader = SSTableReader.Open(outPath, _blockCache);
        }
        catch
        {
            TryDeleteFile(outPath);
            throw;
        }

        // ── Commit through the writer mailbox (view + manifest) ───────────────
        List<SSTableReader> removed;
        try
        {
            removed = _db.CommitCompaction(level, sources, outPath, newReader);
        }
        catch
        {
            newReader.Dispose();
            TryDeleteFile(outPath);
            throw;
        }

        // ── Reclaim (no reader can still reference the retired readers) ───────
        foreach (var p in sources) _blockCache?.EvictFile(p);

        _db.WaitForReadersToDrain();

        foreach (var r in removed) r.Dispose();
        foreach (var p in sources)
        {
            try { File.Delete(p); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _options.DiagnosticLog?.Invoke(
                    $"[PithosDB.Compaction] source delete deferred to next open '{p}': {ex.Message}");
            }
        }

        return true;
    }

    private void TryDeleteFile(string path)
    {
        if (!File.Exists(path)) return;
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _options.DiagnosticLog?.Invoke(
                $"[PithosDB.Compaction] orphan delete deferred to next open '{path}': {ex.Message}");
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
                        else userValue = decoded ?? throw new InvalidOperationException("[Compaction] TTL 解码返回空值但未标记丢弃");
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
