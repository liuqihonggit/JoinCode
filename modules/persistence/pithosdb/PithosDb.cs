using System.Runtime.CompilerServices;
using PithosDB.Core.Compaction;
using PithosDB.Core.Core;
using PithosDB.Core.Storage;

namespace PithosDB.Core;

/// <summary>
/// Embedded key-value store backed by an LSM-tree. Writes are buffered in a
/// <see cref="MemTable"/>, durably recorded in a <see cref="WriteAheadLog"/>,
/// and periodically flushed to immutable <see cref="SSTableWriter">SSTables</see>
/// on disk. All public operations are thread-safe: concurrent reads proceed in
/// parallel; a write briefly blocks all readers while the MemTable and level
/// list are updated.
/// </summary>
public sealed class PithosDb : IDisposable
{
    private readonly string _directory;
    private readonly PithosOptions _options;
    private readonly LeveledCompactor _compactor;
    private readonly List<List<string>> _levels = [];
    private readonly Dictionary<string, SSTableReader> _readerCache;
    private readonly IBlockCache? _blockCache;
    private readonly Manifest _manifest;
    private readonly ReaderWriterLockSlim _lock;

    // Background compaction
    private readonly SemaphoreSlim _compactionSignal = new(0, 1);
    private readonly CancellationTokenSource _compactionCts = new();
    private Thread? _compactionThread;

    private MemTable _memTable = new();
    private WriteAheadLog? _wal;

    /// <summary>
    /// Opens or creates a database in <paramref name="directory"/>. Any unflushed
    /// WAL entries are replayed and existing SSTable files are recovered into the
    /// level structure before the instance is returned.
    /// </summary>
    /// <param name="directory">Path to the database directory. Created if it does not exist.</param>
    /// <param name="options">
    /// Tuning options. Pass <see langword="null"/> or omit to use <see cref="PithosOptions.Default"/>.
    /// </param>
    /// <remarks>
    /// 工厂方法（ADR 0129）：所有资源（blockCache/manifest/compactor/wal）在工厂内逐步创建，
    /// 任一失败时释放已分配资源；RecoverFromWal/RecoverSSTables/后台线程在对象构造后执行，
    /// 失败时 db.Dispose() 释放全部。构造函数仅做字段赋值，禁止半构造化。
    /// </remarks>
    public static PithosDb Open(string directory, PithosOptions? options = null)
    {
        options = options ?? PithosOptions.Default;
        options.Validate();

        if (!options.InMemory)
            Directory.CreateDirectory(directory);

        PithosDb? db = null;
        IBlockCache? blockCache = null;
        Manifest? manifest = null;
        Dictionary<string, SSTableReader>? readerCache = null;
        ReaderWriterLockSlim? rwLock = null;
        LeveledCompactor? compactor = null;
        WriteAheadLog? wal = null;
        try
        {
            blockCache = options.BlockCacheSizeBytes > 0
                ? options.BlockCacheKind == BlockCacheKind.S3Fifo
                    ? new S3FifoBlockCache(options.BlockCacheSizeBytes)
                    : new LruBlockCache(options.BlockCacheSizeBytes)
                : null;
            manifest = new Manifest(directory);
            readerCache = new Dictionary<string, SSTableReader>();
            rwLock = new ReaderWriterLockSlim();
            compactor = new LeveledCompactor(directory, options, readerCache, blockCache, manifest, rwLock);
            if (!options.InMemory)
                wal = WriteAheadLog.Open(Path.Combine(directory, "wal.log"), options.WalSyncMode, options.WalSyncIntervalMs);

            db = new PithosDb(directory, options, blockCache, manifest, readerCache, rwLock, compactor, wal);

            if (!options.InMemory)
            {
                db.RecoverFromWal();
                db.RecoverSSTables();
                if (!options.DisableCompaction)
                {
                    db._compactionThread = new Thread(db.CompactionLoop) { IsBackground = true, Name = "PithosDB-Compaction" };
                    db._compactionThread.Start();
                    // Trigger an initial check in case recovered levels already need compaction.
                    db.SignalCompaction();
                }
            }

            return db;
        }
        catch
        {
            db?.Dispose();
            if (db is null)
            {
                wal?.Dispose();
                rwLock?.Dispose();
                if (readerCache is not null)
                    foreach (var r in readerCache.Values) r.Dispose();
                (blockCache as IDisposable)?.Dispose();
            }
            throw;
        }
    }

    /// <summary>
    /// 私有构造函数 — 仅字段赋值，不执行任何可能抛异常的 IO 或恢复逻辑（ADR 0129）。
    /// </summary>
    private PithosDb(string directory, PithosOptions options, IBlockCache? blockCache, Manifest manifest,
        Dictionary<string, SSTableReader> readerCache, ReaderWriterLockSlim rwLock, LeveledCompactor compactor, WriteAheadLog? wal)
    {
        _directory = directory;
        _options = options;
        _blockCache = blockCache;
        _manifest = manifest;
        _readerCache = readerCache;
        _lock = rwLock;
        _compactor = compactor;
        _wal = wal;
    }

    /// <summary>
    /// Inserts or updates <paramref name="key"/> with <paramref name="value"/>.
    /// The write is appended to the WAL before being applied to the MemTable.
    /// A MemTable flush (and possible compaction) is triggered when the buffer
    /// exceeds <see cref="PithosOptions.MemTableSizeThreshold"/>.
    /// </summary>
    public void Put(byte[] key, byte[] value)
    {
        var stored = _options.EnableTtl ? ValueCodec.Encode(value) : value;
        _lock.EnterWriteLock();
        try
        {
            _wal?.AppendPut(key, stored);
            _memTable.Put(key, stored);
            MaybeFlushMemTable();
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Inserts or updates <paramref name="key"/> with <paramref name="value"/>, expiring
    /// it after <paramref name="ttl"/> elapses. The entry becomes invisible to reads as
    /// soon as the TTL expires and is physically removed during the next compaction.
    /// Requires <see cref="PithosOptions.EnableTtl"/> to be <see langword="true"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <see cref="PithosOptions.EnableTtl"/> is <see langword="false"/>.
    /// </exception>
    public void Put(byte[] key, byte[] value, TimeSpan ttl)
    {
        if (!_options.EnableTtl)
            throw new InvalidOperationException(
                "TTL writes require PithosOptions.EnableTtl = true.");

        var stored = ValueCodec.EncodeWithExpiry(value, DateTimeOffset.UtcNow.Add(ttl));
        _lock.EnterWriteLock();
        try
        {
            _wal?.AppendPut(key, stored);
            _memTable.Put(key, stored);
            MaybeFlushMemTable();
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Applies all operations in <paramref name="batch"/> atomically. The entire
    /// batch is written to the WAL as a single CRC-guarded record before any
    /// MemTable mutation occurs, so either every operation survives a crash or none do.
    /// </summary>
    public void Write(WriteBatch batch)
    {
        if (batch.Operations.Count == 0) return;
        _lock.EnterWriteLock();
        try { ApplyBatch(batch); }
        finally { _lock.ExitWriteLock(); }
    }

    // Encodes, WAL-appends, and MemTable-applies a batch. Caller must hold the write lock.
    private void ApplyBatch(WriteBatch batch)
    {
        if (batch.Operations.Count == 0) return;

        var encoded = batch.Operations
            .Select(op =>
            {
                if (op.Type != WalEntryType.Put) return (op.Type, op.Key, op.Value);
                if (op.Ttl.HasValue)
                {
                    if (!_options.EnableTtl)
                        throw new InvalidOperationException(
                            "TTL batch entries require PithosOptions.EnableTtl = true.");
                    return (op.Type, op.Key,
                        (byte[]?)ValueCodec.EncodeWithExpiry(op.Value!, DateTimeOffset.UtcNow.Add(op.Ttl.Value)));
                }
                return (op.Type, op.Key,
                    (byte[]?)(_options.EnableTtl ? ValueCodec.Encode(op.Value!) : op.Value));
            })
            .ToList();

        _wal?.AppendBatch(encoded);
        foreach (var (type, key, value) in encoded)
        {
            if (type == WalEntryType.Put) _memTable.Put(key, value!);
            else _memTable.Delete(key);
        }
        MaybeFlushMemTable();
    }

    /// <summary>
    /// Deletes <paramref name="key"/> by writing a tombstone. Subsequent
    /// <see cref="TryGet"/> calls for this key return <see langword="false"/>
    /// until a new value is written. The tombstone is physically removed during
    /// compaction.
    /// </summary>
    public void Delete(byte[] key)
    {
        _lock.EnterWriteLock();
        try
        {
            _wal?.AppendDelete(key);
            _memTable.Delete(key);
            MaybeFlushMemTable();
        }
        finally { _lock.ExitWriteLock(); }
    }

    /// <summary>
    /// Deletes all live keys in the inclusive range [<paramref name="from"/>,
    /// <paramref name="to"/>] by writing tombstones in a single atomic
    /// <see cref="WriteBatch"/>. Omit either bound for an open-ended range;
    /// omit both to delete every key in the database. Keys inserted concurrently
    /// between the scan and the batch write are not affected.
    /// </summary>
    public void DeleteRange(byte[]? from = null, byte[]? to = null)
    {
        // Phase 1: collect keys under a read lock so the scan doesn't block writers.
        _lock.EnterReadLock();
        List<byte[]> keys;
        try { keys = CollectScan(from, to).Select(e => e.key).ToList(); }
        finally { _lock.ExitReadLock(); }

        if (keys.Count == 0) return;

        // Phase 2: write all tombstones atomically.
        var batch = new WriteBatch();
        foreach (var key in keys) batch.Delete(key);
        Write(batch);
    }

    /// <summary>
    /// Atomically reads <paramref name="key"/> and, if its current value equals
    /// <paramref name="expectedValue"/>, replaces it with <paramref name="newValue"/>.
    /// Returns <see langword="true"/> when the swap was applied, <see langword="false"/>
    /// when the current value did not match and no write was performed.
    /// <para>
    /// Pass <see langword="null"/> for <paramref name="expectedValue"/> to match a key
    /// that does not exist or has been deleted — useful for "insert if absent" semantics.
    /// </para>
    /// <para>
    /// The read and the conditional write are performed under a single write lock, so no
    /// concurrent writer can observe an intermediate state.
    /// </para>
    /// </summary>
    public bool CompareAndSwap(byte[] key, byte[]? expectedValue, byte[] newValue)
    {
        _lock.EnterWriteLock();
        try
        {
            var current = ReadCurrentValue(key);
            if (!BytesEqual(current, expectedValue)) return false;

            var stored = _options.EnableTtl ? ValueCodec.Encode(newValue) : newValue;
            _wal?.AppendPut(key, stored);
            _memTable.Put(key, stored);
            MaybeFlushMemTable();
            return true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    // Reads the decoded user value for 'key' without acquiring any lock.
    // Must only be called while the caller already holds _lock (read or write).
    private byte[]? ReadCurrentValue(byte[] key)
    {
        byte[]? raw;
        if (_memTable.TryGet(key, out raw))
        {
            if (raw is null) return null; // tombstone
            return DecodeAndFilter(key, raw, out var v) ? v : null;
        }
        foreach (var level in _levels)
        foreach (var path in Enumerable.Reverse(level))
        {
            if (_readerCache[path].TryGet(key, out raw))
            {
                if (raw is null) return null; // tombstone
                return DecodeAndFilter(key, raw, out var v) ? v : null;
            }
        }
        return null;
    }

    private static bool BytesEqual(byte[]? a, byte[]? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return a.AsSpan().SequenceEqual(b.AsSpan());
    }

    /// <summary>
    /// Looks up <paramref name="key"/>, searching the MemTable first, then each
    /// SSTable level from newest to oldest. Returns <see langword="false"/> if
    /// the key does not exist, has been deleted, has expired (TTL), or is filtered
    /// by <see cref="PithosOptions.CompactionFilter"/>. Multiple threads may call
    /// <see cref="TryGet"/> concurrently.
    /// </summary>
    public bool TryGet(byte[] key, out byte[]? value)
    {
        _lock.EnterReadLock();
        try
        {
            byte[]? raw;
            if (_memTable.TryGet(key, out raw))
            {
                if (raw is null) { value = null; return false; } // tombstone
                return DecodeAndFilter(key, raw, out value);
            }

            foreach (var level in _levels)
            foreach (var sstPath in Enumerable.Reverse(level))
            {
                if (_readerCache[sstPath].TryGet(key, out raw))
                {
                    if (raw is null) { value = null; return false; } // tombstone
                    return DecodeAndFilter(key, raw, out value);
                }
            }

            value = null;
            return false;
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Returns <see langword="true"/> if <paramref name="key"/> exists and has not been
    /// deleted, expired, or excluded by <see cref="PithosOptions.CompactionFilter"/>.
    /// Equivalent to <see cref="TryGet"/> without materialising the value — useful when
    /// only key presence needs to be confirmed.
    /// </summary>
    public bool KeyExists(byte[] key) => TryGet(key, out _);

    /// <summary>
    /// Asynchronously returns <see langword="true"/> if <paramref name="key"/> exists.
    /// </summary>
    public Task<bool> KeyExistsAsync(byte[] key, CancellationToken cancellationToken = default)
        => Task.Run(() => KeyExists(key), cancellationToken);

    /// <summary>
    /// Returns all live key-value pairs whose keys fall within the inclusive range
    /// [<paramref name="from"/>, <paramref name="to"/>], in sorted order. Omit either
    /// bound for an open-ended scan; omit both for a full scan. Expired and filtered
    /// entries are excluded.
    /// </summary>
    public IEnumerable<(byte[] key, byte[] value)> Scan(byte[]? from = null, byte[]? to = null)
    {
        _lock.EnterReadLock();
        try
        {
            return CollectScan(from, to);
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Returns an approximate count of live keys in the inclusive range
    /// [<paramref name="from"/>, <paramref name="to"/>]. Omit either bound for an
    /// open-ended range; omit both for the full database.
    /// <para>
    /// The count is exact for keys in the MemTable and a block-level approximation
    /// for keys in SSTables — each SSTable block that overlaps the range contributes
    /// one unit regardless of how many keys it contains. The result may overcount
    /// when the same key exists in both the MemTable and an SSTable (before the next
    /// compaction), and does not reflect tombstones that shadow SSTable values.
    /// Use this for query planning and informational stats, not for exact counts.
    /// </para>
    /// </summary>
    public long ApproximateCount(byte[]? from = null, byte[]? to = null)
    {
        var cmp = ByteArrayComparer.Instance;
        _lock.EnterReadLock();
        try
        {
            long count = 0;

            // Exact live count from the MemTable.
            foreach (var kv in _memTable.GetSortedEntries())
            {
                if (from != null && cmp.Compare(kv.Key, from) < 0) continue;
                if (to   != null && cmp.Compare(kv.Key, to)   > 0) break;
                if (kv.Value != null) count++; // skip tombstones
            }

            // Block-level approximation from SSTables across all levels.
            foreach (var level in _levels)
                foreach (var path in level)
                    count += _readerCache[path].ApproximateKeyCount(from, to);

            return count;
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Returns all live key-value pairs whose keys begin with <paramref name="prefix"/>,
    /// in sorted order. An empty prefix matches every key (equivalent to a full scan).
    /// Expired and filtered entries are excluded.
    /// </summary>
    public IEnumerable<(byte[] key, byte[] value)> ScanPrefix(byte[] prefix)
    {
        if (prefix.Length == 0) return Scan();

        byte[]? upper = ComputePrefixUpperBound(prefix);

        _lock.EnterReadLock();
        try
        {
            return CollectScan(prefix, upper)
                .Where(e => e.key.AsSpan().StartsWith(prefix));
        }
        finally { _lock.ExitReadLock(); }
    }

    // Returns the smallest key that is lexicographically greater than every key
    // starting with 'prefix', used as the inclusive upper bound for a prefix scan.
    // Increments the rightmost non-0xFF byte and truncates everything after it.
    // Returns null when all bytes are 0xFF (prefix spans the entire key space).
    private static byte[]? ComputePrefixUpperBound(byte[] prefix)
    {
        for (int i = prefix.Length - 1; i >= 0; i--)
        {
            if (prefix[i] < 0xFF)
            {
                var upper = prefix[0..(i + 1)];
                upper[i]++;
                return upper;
            }
        }
        return null; // all bytes are 0xFF — no upper bound exists
    }

    /// <summary>
    /// Returns all live key-value pairs in the inclusive range
    /// [<paramref name="from"/>, <paramref name="to"/>] in descending key order.
    /// Omit either bound for an open-ended scan; omit both for a full reverse scan.
    /// Expired and filtered entries are excluded.
    /// </summary>
    public IEnumerable<(byte[] key, byte[] value)> ScanDescending(byte[]? from = null, byte[]? to = null)
    {
        _lock.EnterReadLock();
        try
        {
            var results = CollectScan(from, to);
            results.Reverse();
            return results;
        }
        finally { _lock.ExitReadLock(); }
    }

    // Strips the TTL header (when EnableTtl), checks expiry, and applies the
    // compaction filter. Returns false if the entry should be hidden from the caller.
    private bool DecodeAndFilter(byte[] key, byte[] raw, out byte[]? value)
    {
        if (_options.EnableTtl)
        {
            value = ValueCodec.Decode(raw);
            if (value is null) return false; // expired
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

        var sources = new List<IEnumerable<KeyValuePair<byte[], byte[]?>>>
        {
            _memTable.GetSortedEntries()
        };
        foreach (var level in _levels)
            foreach (var path in Enumerable.Reverse(level))
                sources.Add(_readerCache[path].ReadAllEntries());

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

            // Decode TTL wrapper and check expiry.
            byte[] userValue;
            if (_options.EnableTtl)
            {
                var decoded = ValueCodec.Decode(value);
                if (decoded is null) continue; // expired
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

    private void MaybeFlushMemTable()
    {
        if (_options.InMemory) return;
        if (_memTable.SizeBytes < _options.MemTableSizeThreshold) return;

        if (_levels.Count == 0) _levels.Add([]);

        string sstPath = Path.Combine(_directory, $"L0_{Guid.NewGuid():N}.sst");
        SSTableWriter.Write(sstPath, _memTable.GetSortedEntries(), _options.BloomFilterFalsePositiveRate, _options.Compression);
        _levels[0].Add(sstPath);
        _readerCache[sstPath] = SSTableReader.Open(sstPath, _blockCache);
        _memTable.Clear();

        _wal!.Dispose();
        File.Delete(Path.Combine(_directory, "wal.log"));
        _wal = WriteAheadLog.Open(Path.Combine(_directory, "wal.log"), _options.WalSyncMode, _options.WalSyncIntervalMs);

        _manifest.Write(_levels);
        SignalCompaction();
    }

    private void SignalCompaction()
    {
        if (_options.DisableCompaction) return;
        // Release at most one token so the background thread wakes up.
        // SemaphoreFullException means a signal is already pending — that's fine.
        try { _compactionSignal.Release(); } catch (SemaphoreFullException) { }
    }

    private void CompactionLoop()
    {
        while (true)
        {
            try { _compactionSignal.Wait(_compactionCts.Token); }
            catch (OperationCanceledException) { return; }

            // Drain all pending work before sleeping again. A single
            // CompactIfNeeded pass may make a previously-under-limit level
            // go over its limit (L0→L1 can push L1 over its limit), so we
            // loop until no level needs compaction.
            try
            {
                while (_compactor.CompactIfNeeded(_levels)
                       && !_compactionCts.IsCancellationRequested) { }
            }
            catch { /* exceptions must not kill the compaction thread */ }
        }
    }

    private void RecoverFromWal()
    {
        foreach (var (type, key, value) in WriteAheadLog.Replay(Path.Combine(_directory, "wal.log")))
        {
            if (type == WalEntryType.Put) _memTable.Put(key, value!);
            else _memTable.Delete(key);
        }
    }

    private void RecoverSSTables()
    {
        var log = _options.DiagnosticLog;
        foreach (var path in Directory.GetFiles(_directory, "*.sst"))
        {
            if (new FileInfo(path).Length == 0)
            {
                log?.Invoke($"[PithosDB.RecoverSSTables] deleting empty SST file: {path}");
                try { File.Delete(path); } catch { }
            }
        }

        if (_manifest.TryRead(out var manifestLevels))
        {
            for (int i = 0; i < manifestLevels.Count; i++)
            {
                while (_levels.Count <= i) _levels.Add([]);
                foreach (var path in manifestLevels[i])
                {
                    if (!File.Exists(path)) continue;
                    if (new FileInfo(path).Length == 0) continue;
                    _levels[i].Add(path);
                    _readerCache[path] = SSTableReader.Open(path, _blockCache);
                }
            }

            var knownFiles = new HashSet<string>(
                manifestLevels.SelectMany(l => l).Select(p => Path.GetFullPath(p)),
                StringComparer.OrdinalIgnoreCase);
            foreach (var path in Directory.GetFiles(_directory, "*.sst"))
            {
                if (!knownFiles.Contains(Path.GetFullPath(path)))
                {
                    log?.Invoke($"[PithosDB.RecoverSSTables] deleting orphaned SST file not in manifest: {path}");
                    File.Delete(path);
                }
            }
        }
        else
        {
            foreach (var path in Directory.GetFiles(_directory, "*.sst").OrderBy(File.GetCreationTimeUtc))
            {
                if (new FileInfo(path).Length == 0) continue;
                var name = Path.GetFileNameWithoutExtension(path);
                var sep = name.IndexOf('_');
                if (sep < 2 || !name.StartsWith('L')) continue;
                if (!int.TryParse(name[1..sep], out int level)) continue;

                while (_levels.Count <= level) _levels.Add([]);
                _levels[level].Add(path);
                _readerCache[path] = SSTableReader.Open(path, _blockCache);
            }

            if (_levels.Count > 0)
                _manifest.Write(_levels);
        }
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a point-in-time <see cref="DbStats"/> snapshot: MemTable usage,
    /// per-level SSTable file counts and on-disk sizes, block cache occupancy,
    /// and WAL size. Acquiring the read lock ensures a consistent view; the call
    /// is fast for typical level counts but does perform one
    /// <see cref="FileInfo"/> lookup per SSTable file.
    /// </summary>
    public DbStats GetStats()
    {
        _lock.EnterReadLock();
        try
        {
            var fileCounts = _levels.Select(l => l.Count).ToList();
            var diskSizes  = _levels
                .Select(l => l.Sum(p => File.Exists(p) ? new FileInfo(p).Length : 0L))
                .ToList();

            long walSize = -1;
            if (!_options.InMemory)
            {
                var walPath = Path.Combine(_directory, "wal.log");
                walSize = File.Exists(walPath) ? new FileInfo(walPath).Length : 0L;
            }

            return new DbStats
            {
                MemTableSizeBytes        = _memTable.SizeBytes,
                MemTableEntryCount       = _memTable.Count,
                LevelCount               = _levels.Count,
                FileCountPerLevel        = fileCounts,
                DiskSizeBytesPerLevel    = diskSizes,
                TotalSstFileCount        = fileCounts.Sum(),
                TotalSstDiskSizeBytes    = diskSizes.Sum(),
                BlockCacheCurrentSizeBytes = _blockCache?.CurrentSizeBytes ?? -1L,
                BlockCacheMaxSizeBytes     = _blockCache is not null ? _options.BlockCacheSizeBytes : -1L,
                WalSizeBytes             = walSize,
            };
        }
        finally { _lock.ExitReadLock(); }
    }

    /// <summary>
    /// Asynchronously returns a <see cref="DbStats"/> snapshot.
    /// </summary>
    public Task<DbStats> GetStatsAsync(CancellationToken cancellationToken = default)
        => Task.Run(GetStats, cancellationToken);

    // ── Transactions ──────────────────────────────────────────────────────────

    /// <summary>
    /// Begins a new optimistic <see cref="Transaction"/>. A point-in-time
    /// <see cref="Snapshot"/> is taken at this moment to provide consistent reads
    /// throughout the transaction's lifetime. Dispose or <see cref="Transaction.Rollback"/>
    /// the returned transaction if it is not committed.
    /// </summary>
    public Transaction BeginTransaction() => new Transaction(this, GetSnapshot());

    // Validates the read set and applies the write batch atomically under the write lock.
    // Returns false (without applying any writes) if a conflict is detected.
    internal bool TryCommitTransaction(
        Snapshot snapshot,
        IReadOnlyCollection<byte[]> readKeys,
        WriteBatch batch)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var key in readKeys)
            {
                var current = ReadCurrentValue(key);
                snapshot.TryGet(key, out var snapVal);
                if (!BytesEqual(current, snapVal)) return false;
            }
            ApplyBatch(batch);
            return true;
        }
        finally { _lock.ExitWriteLock(); }
    }

    // ── Snapshot ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a point-in-time <see cref="Snapshot"/> of the database. The snapshot
    /// reflects the exact state at the moment this method is called and remains
    /// consistent regardless of subsequent writes or compactions. Dispose the snapshot
    /// when finished to release its file handles.
    /// <para>
    /// The snapshot opens independent <see cref="Storage.SSTableReader"/> instances so
    /// it does not share file handles with the live database. On Linux, open handles
    /// keep file inodes alive after compaction removes them; on Windows, compaction
    /// deletions silently fail and orphaned files are cleaned up on the next database
    /// open.
    /// </para>
    /// </summary>
    public Snapshot GetSnapshot()
    {
        _lock.EnterReadLock();
        try
        {
            // Copy the MemTable into an independent sorted dictionary.
            var mem = new SortedDictionary<byte[], byte[]?>(ByteArrayComparer.Instance);
            foreach (var kv in _memTable.GetSortedEntries())
                mem[kv.Key] = kv.Value;

            // Capture the level structure and open dedicated readers for each SSTable.
            // These readers are independent of the shared cache so the snapshot remains
            // readable even after compaction recycles the shared cache entries.
            var levelPaths = _levels.Select(l => l.ToList()).ToList();
            var readers = new Dictionary<string, SSTableReader>(StringComparer.Ordinal);
            foreach (var path in levelPaths.SelectMany(l => l))
                readers[path] = SSTableReader.Open(path);

            return new Snapshot(mem, levelPaths, readers, _options);
        }
        finally { _lock.ExitReadLock(); }
    }

    // ── In-memory factory ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates an in-memory <see cref="PithosDb"/> that stores no data on disk.
    /// No directory is required; data is lost when the instance is disposed.
    /// Useful for unit testing and ephemeral workloads.
    /// </summary>
    /// <param name="options">
    /// Optional tuning options. <see cref="PithosOptions.InMemory"/> is forced to
    /// <see langword="true"/>; disk-related settings (block cache, compression, etc.)
    /// are ignored. Pass <see langword="null"/> to use defaults.
    /// </param>
    public static PithosDb OpenInMemory(PithosOptions? options = null)
    {
        if (options is not null && !options.InMemory)
            throw new ArgumentException(
                $"Options passed to {nameof(OpenInMemory)} must have {nameof(PithosOptions.InMemory)} = true.",
                nameof(options));

        return Open(":memory:", options ?? new PithosOptions { InMemory = true });
    }

    // ── Async API ──────────────────────────────────────────────────────────────
    //
    // ReaderWriterLockSlim has thread affinity and cannot span an await, so each
    // async method offloads its synchronous counterpart to the thread pool via
    // Task.Run. This frees the caller's thread while WAL fsyncs and SSTable reads
    // are in progress — the primary value for ASP.NET and other async-first hosts.

    /// <summary>
    /// Asynchronously inserts or updates <paramref name="key"/> with <paramref name="value"/>.
    /// </summary>
    public Task PutAsync(byte[] key, byte[] value, CancellationToken cancellationToken = default)
        => Task.Run(() => Put(key, value), cancellationToken);

    /// <summary>
    /// Asynchronously inserts or updates <paramref name="key"/> with <paramref name="value"/>,
    /// expiring after <paramref name="ttl"/> elapses.
    /// Requires <see cref="PithosOptions.EnableTtl"/> to be <see langword="true"/>.
    /// </summary>
    public Task PutAsync(byte[] key, byte[] value, TimeSpan ttl, CancellationToken cancellationToken = default)
        => Task.Run(() => Put(key, value, ttl), cancellationToken);

    /// <summary>
    /// Asynchronously looks up <paramref name="key"/>. Returns the value, or
    /// <see langword="null"/> if the key does not exist, has been deleted, has expired,
    /// or is excluded by the compaction filter.
    /// </summary>
    public Task<byte[]?> GetAsync(byte[] key, CancellationToken cancellationToken = default)
        => Task.Run<byte[]?>(() => TryGet(key, out var v) ? v : null, cancellationToken);

    /// <summary>
    /// Asynchronously deletes <paramref name="key"/> by writing a tombstone.
    /// </summary>
    public Task DeleteAsync(byte[] key, CancellationToken cancellationToken = default)
        => Task.Run(() => Delete(key), cancellationToken);

    /// <summary>
    /// Asynchronously deletes all live keys in the inclusive range
    /// [<paramref name="from"/>, <paramref name="to"/>].
    /// </summary>
    public Task DeleteRangeAsync(byte[]? from = null, byte[]? to = null, CancellationToken cancellationToken = default)
        => Task.Run(() => DeleteRange(from, to), cancellationToken);

    /// <summary>
    /// Asynchronously performs a compare-and-swap on <paramref name="key"/>.
    /// Returns <see langword="true"/> when the swap was applied.
    /// </summary>
    public Task<bool> CompareAndSwapAsync(byte[] key, byte[]? expectedValue, byte[] newValue,
        CancellationToken cancellationToken = default)
        => Task.Run(() => CompareAndSwap(key, expectedValue, newValue), cancellationToken);

    /// <summary>
    /// Asynchronously applies all operations in <paramref name="batch"/> atomically.
    /// </summary>
    public Task WriteAsync(WriteBatch batch, CancellationToken cancellationToken = default)
        => Task.Run(() => Write(batch), cancellationToken);

    /// <summary>
    /// Asynchronously returns all live key-value pairs whose keys begin with
    /// <paramref name="prefix"/>, in sorted order.
    /// </summary>
    public async IAsyncEnumerable<(byte[] key, byte[] value)> ScanPrefixAsync(
        byte[] prefix,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var results = await Task.Run(() => ScanPrefix(prefix).ToList(), cancellationToken)
                                .ConfigureAwait(false);
        foreach (var entry in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    /// <summary>
    /// Asynchronously streams all live key-value pairs in the inclusive range
    /// [<paramref name="from"/>, <paramref name="to"/>] in descending key order.
    /// Omit either bound for an open-ended scan; omit both for a full reverse scan.
    /// </summary>
    public async IAsyncEnumerable<(byte[] key, byte[] value)> ScanDescendingAsync(
        byte[]? from = null,
        byte[]? to = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var results = await Task.Run(() => ScanDescending(from, to).ToList(), cancellationToken)
                                .ConfigureAwait(false);
        foreach (var entry in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    /// <summary>
    /// Asynchronously streams all live key-value pairs whose keys fall within the
    /// inclusive range [<paramref name="from"/>, <paramref name="to"/>], in sorted order.
    /// Omit either bound for an open-ended scan; omit both for a full scan.
    /// </summary>
    public async IAsyncEnumerable<(byte[] key, byte[] value)> ScanAsync(
        byte[]? from = null,
        byte[]? to = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var results = await Task.Run(() => Scan(from, to).ToList(), cancellationToken)
                                .ConfigureAwait(false);
        foreach (var entry in results)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    /// <summary>
    /// Stops the background compaction thread, flushes and closes the WAL,
    /// disposes all cached SSTable readers, then disposes the lock.
    /// Any compaction already in progress is allowed to finish before returning.
    /// </summary>
    public void Dispose()
    {
        // Signal the background thread to stop. If it is mid-compaction it will
        // finish the current job before seeing the cancellation.
        _compactionCts.Cancel();
        SignalCompaction(); // unblock it if it is waiting on the semaphore
        _compactionThread?.Join();
        _compactionCts.Dispose();
        _compactionSignal.Dispose();

        _wal?.Dispose();
        foreach (var reader in _readerCache.Values)
            reader.Dispose();
        _readerCache.Clear();
        _lock.Dispose();
    }
}
