





namespace PithosDB.Core;

/// <summary>
/// Embedded key-value store backed by an LSM-tree. Writes are buffered in a
/// <see cref="MemTable"/>, durably recorded in a <see cref="WriteAheadLog"/>,
/// and periodically flushed to immutable <see cref="SSTableWriter">SSTables</see>
/// on disk.
/// <para>
/// Concurrency model (lock-free):
/// <list type="bullet">
/// <item>All mutations are serialised by a single-writer mailbox
/// (<see cref="Channel{T}"/> → dedicated writer thread), which preserves WAL ordering,
/// compare-and-swap atomicity and transaction commit atomicity without holding a lock.</item>
/// <item>Readers capture the current immutable <c>DbView</c> (level listing + shared
/// readers + active MemTable) with one volatile read and never block; flush and
/// compaction publish a brand-new view instead of mutating the old one.</item>
/// <item>Flush swaps the active MemTable instead of clearing it, so readers that
/// captured the previous view keep seeing a complete dataset.</item>
/// <item>Retired SSTable readers and files are reclaimed only after every in-flight
/// reader has drained (active-reader epoch), so compaction never frees memory or files
/// still referenced by a read.</item>
/// </list>
/// </para>
/// </summary>
public sealed class PithosDb : IDisposable
{
    private readonly string _directory;
    private readonly PithosOptions _options;
    private readonly LeveledCompactor _compactor;
    private readonly IBlockCache? _blockCache;
    private readonly Manifest _manifest;

    /// <summary>
    /// Immutable state snapshot: level listing, one shared reader per SSTable, and the
    /// active MemTable. Never mutated after publication — writers build a new instance
    /// and publish it with <see cref="Volatile.Write{T}(ref T, T)"/>.
    /// </summary>
    private sealed class DbView
    {
        public List<List<string>> Levels { get; }
        public Dictionary<string, SSTableReader> Readers { get; }
        public MemTable ActiveMemTable { get; }

        public DbView(List<List<string>> levels, Dictionary<string, SSTableReader> readers, MemTable activeMemTable)
        {
            Levels = levels;
            Readers = readers;
            ActiveMemTable = activeMemTable;
        }

        public List<List<string>> CloneLevels() => Levels.Select(level => new List<string>(level)).ToList();

        public Dictionary<string, SSTableReader> CloneReaders() => new(Readers, StringComparer.Ordinal);
    }

    /// <summary>
    /// Read-epoch guard — readers increment the epoch before capturing the view and
    /// decrement it when the operation completes. The compactor waits for the epoch to
    /// drain before disposing retired readers or deleting source SSTables.
    /// </summary>
    private readonly struct ReadScope : IDisposable
    {
        private readonly PithosDb _db;
        public DbView View { get; }

        public ReadScope(PithosDb db)
        {
            _db = db;
            View = Volatile.Read(ref db._view);
        }

        public void Dispose() => Interlocked.Decrement(ref _db._activeReads);
    }

    private DbView _view;
    private int _activeReads;

    // Single-writer mailbox — every mutation and every view publication runs on the
    // writer thread, in FIFO order. This replaces the former ReaderWriterLockSlim.
    // style-allow: unbounded — 写邮箱由任意读线程投递,有界会阻塞读线程等待写线程导致锁反转
    private readonly Channel<Action> _writeQueue = Channel.CreateUnbounded<Action>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private Thread? _writerThread;

    // Background compaction wake signal — bounded channel (capacity 1, drop-write)
    // replaces the former SemaphoreSlim(0,1) + SemaphoreFullException probe.
    private readonly Channel<bool> _compactionSignal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false,
        });
    private readonly CancellationTokenSource _compactionCts = new();
    private Thread? _compactionThread;

    private WriteAheadLog? _wal;
    private int _disposed;

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
    /// 工厂方法（ADR 0129）：所有资源（blockCache/manifest/wal）在工厂内逐步创建，
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
        WriteAheadLog? wal = null;
        try
        {
            blockCache = options.BlockCacheSizeBytes > 0
                ? options.BlockCacheKind == BlockCacheKind.S3Fifo
                    ? new S3FifoBlockCache(options.BlockCacheSizeBytes)
                    : new LruBlockCache(options.BlockCacheSizeBytes)
                : null;
            manifest = new Manifest(directory);
            if (!options.InMemory)
                wal = WriteAheadLog.Open(Path.Combine(directory, "wal.log"), options.WalSyncMode, options.WalSyncIntervalMs);

            db = new PithosDb(directory, options, blockCache, manifest, wal);

            if (!options.InMemory)
            {
                db.RecoverFromWal();
                db.RecoverSSTables();
            }

            db.StartBackgroundThreads();

            // Trigger an initial check in case recovered levels already need compaction.
            if (!options.InMemory) db.SignalCompaction();

            return db;
        }
        catch
        {
            db?.Dispose();
            if (db is null)
            {
                wal?.Dispose();
                (blockCache as IDisposable)?.Dispose();
            }
            throw;
        }
    }

    /// <summary>
    /// 私有构造函数 — 仅字段赋值，不执行任何可能抛异常的 IO 或恢复逻辑（ADR 0129）。
    /// </summary>
    private PithosDb(string directory, PithosOptions options, IBlockCache? blockCache, Manifest manifest, WriteAheadLog? wal)
    {
        _directory = directory;
        _options = options;
        _blockCache = blockCache;
        _manifest = manifest;
        _wal = wal;
        _view = new DbView([], new Dictionary<string, SSTableReader>(StringComparer.Ordinal), new MemTable());
        _compactor = new LeveledCompactor(directory, options, this, blockCache);
    }

    /// <summary>
    /// 释放后访问守卫 — 所有公共增删改查方法入口调用，防止 Dispose 后操作已释放的 WAL/MemTable/SST readers。
    /// </summary>
    /// <exception cref="ObjectDisposedException">实例已 Dispose。</exception>
    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(PithosDb));
    }

    /// <summary>
    /// Current level listing (immutable by convention after publication). Used by the
    /// compactor to decide which level needs work.
    /// </summary>
    internal List<List<string>> CurrentLevels => Volatile.Read(ref _view).Levels;

    // ── Write mailbox ─────────────────────────────────────────────────────────

    private void ExecuteWrite(Action action)
    {
        ThrowIfDisposed();
        EnqueueWriteCore(action);
    }

    private T ExecuteWrite<T>(Func<T> action)
    {
        ThrowIfDisposed();
        return EnqueueWriteCore(action);
    }

    /// <summary>
    /// Enqueues a mutation on the writer thread and blocks until it completes — the
    /// public synchronous API keeps its blocking contract while all serialisation
    /// happens on the mailbox (no lock, no monitor).
    /// </summary>
    private void EnqueueWriteCore(Action action) =>
        EnqueueWriteCore(() => { action(); return true; });

    private T EnqueueWriteCore<T>(Func<T> action)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var enqueued = _writeQueue.Writer.TryWrite(() =>
        {
            try { completion.TrySetResult(action()); }
            catch (Exception ex) { completion.TrySetException(ex); }
        });

        if (!enqueued)
            throw new ObjectDisposedException(nameof(PithosDb));

        return completion.Task.GetAwaiter().GetResult();  // style-allow: sync-block — 同步存储 API 契约: PithosDb 对外同步事务与压缩入口
    }

    private void StartBackgroundThreads()
    {
        _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "PithosDB-Writer" };
        _writerThread.Start();

        if (_options.InMemory || _options.DisableCompaction) return;

        _compactionThread = new Thread(CompactionLoop) { IsBackground = true, Name = "PithosDB-Compaction" };
        _compactionThread.Start();
    }

    private void WriterLoop()
    {
        while (true)
        {
            bool hasWork;
            try { hasWork = _writeQueue.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult(); }  // style-allow: sync-block — 同步存储 API 契约: PithosDb 对外同步事务与压缩入口
            catch (ChannelClosedException) { return; }

            if (!hasWork) return;

            while (_writeQueue.Reader.TryRead(out var operation))
            {
                try { operation(); }
                catch { /* the operation reports failures through its completion source */ }
            }
        }
    }

    // ── Writes ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Inserts or updates <paramref name="key"/> with <paramref name="value"/>.
    /// The write is appended to the WAL before being applied to the MemTable.
    /// A MemTable flush (and possible compaction) is triggered when the buffer
    /// exceeds <see cref="PithosOptions.MemTableSizeThreshold"/>.
    /// </summary>
    public void Put(byte[] key, byte[] value)
    {
        var stored = _options.EnableTtl ? ValueCodec.Encode(value) : value;
        ExecuteWrite(() =>
        {
            var view = Volatile.Read(ref _view);
            _wal?.AppendPut(key, stored);
            view.ActiveMemTable.Put(key, stored);
            MaybeFlushMemTable(view);
        });
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
        ExecuteWrite(() =>
        {
            var view = Volatile.Read(ref _view);
            _wal?.AppendPut(key, stored);
            view.ActiveMemTable.Put(key, stored);
            MaybeFlushMemTable(view);
        });
    }

    /// <summary>
    /// Applies all operations in <paramref name="batch"/> atomically. The entire
    /// batch is written to the WAL as a single CRC-guarded record before any
    /// MemTable mutation occurs, so either every operation survives a crash or none do.
    /// </summary>
    public void Write(WriteBatch batch)
    {
        if (batch.Operations.Count == 0) return;
        ExecuteWrite(() => ApplyBatch(Volatile.Read(ref _view), batch));
    }

    // Encodes, WAL-appends, and MemTable-applies a batch. Caller runs on the writer thread.
    private void ApplyBatch(DbView view, WriteBatch batch)
    {
        if (batch.Operations.Count == 0) return;

        var encoded = batch.Operations
            .Select(op =>
            {
                if (op.Type != WalEntryType.Put) return (op.Type, op.Key, op.Value);
                var putValue = op.Value ?? throw new InvalidOperationException("WAL Put 条目缺少 value");
                if (op.Ttl.HasValue)
                {
                    if (!_options.EnableTtl)
                        throw new InvalidOperationException(
                            "TTL batch entries require PithosOptions.EnableTtl = true.");
                    return (op.Type, op.Key,
                        (byte[]?)ValueCodec.EncodeWithExpiry(putValue, DateTimeOffset.UtcNow.Add(op.Ttl.Value)));
                }
                return (op.Type, op.Key,
                    (byte[]?)(_options.EnableTtl ? ValueCodec.Encode(putValue) : putValue));
            })
            .ToList();

        _wal?.AppendBatch(encoded);
        foreach (var (type, key, value) in encoded)
        {
            if (type == WalEntryType.Put) view.ActiveMemTable.Put(key, value ?? throw new InvalidOperationException("WAL Put 条目缺少 value"));
            else view.ActiveMemTable.Delete(key);
        }
        MaybeFlushMemTable(view);
    }

    /// <summary>
    /// Deletes <paramref name="key"/> by writing a tombstone. Subsequent
    /// <see cref="TryGet"/> calls for this key return <see langword="false"/>
    /// until a new value is written. The tombstone is physically removed during
    /// compaction.
    /// </summary>
    public void Delete(byte[] key)
    {
        ExecuteWrite(() =>
        {
            var view = Volatile.Read(ref _view);
            _wal?.AppendDelete(key);
            view.ActiveMemTable.Delete(key);
            MaybeFlushMemTable(view);
        });
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
        // Phase 1: collect keys without blocking writers.
        List<byte[]> keys;
        using (var scope = BeginRead())
        {
            keys = CollectScan(scope.View, from, to).Select(e => e.key).ToList();
        }

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
    /// The read and the conditional write run as one writer-mailbox operation, so no
    /// concurrent writer can observe an intermediate state.
    /// </para>
    /// </summary>
    public bool CompareAndSwap(byte[] key, byte[]? expectedValue, byte[] newValue) =>
        ExecuteWrite(() =>
        {
            var view = Volatile.Read(ref _view);
            var current = ReadCurrentValue(view, key);
            if (!BytesEqual(current, expectedValue)) return false;

            var stored = _options.EnableTtl ? ValueCodec.Encode(newValue) : newValue;
            _wal?.AppendPut(key, stored);
            view.ActiveMemTable.Put(key, stored);
            MaybeFlushMemTable(view);
            return true;
        });

    // Reads the decoded user value for 'key'. Caller runs on the writer thread.
    private byte[]? ReadCurrentValue(DbView view, byte[] key)
    {
        byte[]? raw;
        if (view.ActiveMemTable.TryGet(key, out raw))
        {
            if (raw is null) return null; // tombstone
            return DecodeAndFilter(key, raw, out var v) ? v : null;
        }
        foreach (var level in view.Levels)
        foreach (var path in Enumerable.Reverse(level))
        {
            if (view.Readers[path].TryGet(key, out raw))
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

    // ── Reads ─────────────────────────────────────────────────────────────────

    private ReadScope BeginRead()
    {
        ThrowIfDisposed();
        Interlocked.Increment(ref _activeReads);
        return new ReadScope(this);
    }

    /// <summary>
    /// Looks up <paramref name="key"/>, searching the MemTable first, then each
    /// SSTable level from newest to oldest. Returns <see langword="false"/> if
    /// the key does not exist, has been deleted, has expired (TTL), or is filtered
    /// by <see cref="PithosOptions.CompactionFilter"/>. Multiple threads may call
    /// <see cref="TryGet"/> concurrently without blocking each other.
    /// </summary>
    public bool TryGet(byte[] key, out byte[]? value)
    {
        using var scope = BeginRead();
        var view = scope.View;

        byte[]? raw;
        if (view.ActiveMemTable.TryGet(key, out raw))
        {
            if (raw is null) { value = null; return false; } // tombstone
            return DecodeAndFilter(key, raw, out value);
        }

        foreach (var level in view.Levels)
        foreach (var sstPath in Enumerable.Reverse(level))
        {
            if (view.Readers[sstPath].TryGet(key, out raw))
            {
                if (raw is null) { value = null; return false; } // tombstone
                return DecodeAndFilter(key, raw, out value);
            }
        }
        value = null;
        return false;
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
        using var scope = BeginRead();
        return CollectScan(scope.View, from, to);
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
        using var scope = BeginRead();
        var view = scope.View;
        var cmp = ByteArrayComparer.Instance;

        long count = 0;

        // Exact live count from the MemTable.
        foreach (var kv in view.ActiveMemTable.GetSortedEntries())
        {
            if (from != null && cmp.Compare(kv.Key, from) < 0) continue;
            if (to   != null && cmp.Compare(kv.Key, to)   > 0) break;
            if (kv.Value != null) count++; // skip tombstones
        }

        // Block-level approximation from SSTables across all levels.
        foreach (var level in view.Levels)
            foreach (var path in level)
                count += view.Readers[path].ApproximateKeyCount(from, to);

        return count;
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

        using var scope = BeginRead();
        return CollectScan(scope.View, prefix, upper)
            .Where(e => e.key.AsSpan().StartsWith(prefix))
            .ToList();
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
    /// Omit either bound for an open-ended range; omit both for a full reverse scan.
    /// Expired and filtered entries are excluded.
    /// </summary>
    public IEnumerable<(byte[] key, byte[] value)> ScanDescending(byte[]? from = null, byte[]? to = null)
    {
        using var scope = BeginRead();
        var results = CollectScan(scope.View, from, to);
        results.Reverse();
        return results;
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

    private List<(byte[] key, byte[] value)> CollectScan(DbView view, byte[]? from, byte[]? to)
    {
        var comparer = ByteArrayComparer.Instance;

        var sources = new List<IEnumerable<KeyValuePair<byte[], byte[]?>>>
        {
            view.ActiveMemTable.GetSortedEntries()
        };
        foreach (var level in view.Levels)
            foreach (var path in Enumerable.Reverse(level))
                sources.Add(view.Readers[path].ReadAllEntries());

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

    // ── Flush / compaction ────────────────────────────────────────────────────

    // Runs on the writer thread. Swaps the active MemTable for a fresh one and
    // publishes a new view; the old MemTable stays alive for readers that captured
    // the previous view and is reclaimed by GC once they finish.
    private void MaybeFlushMemTable(DbView view)
    {
        if (_options.InMemory) return;
        if (view.ActiveMemTable.SizeBytes < _options.MemTableSizeThreshold) return;

        FlushActiveMemTable(view);
    }

    private void FlushActiveMemTable(DbView view)
    {
        string sstPath = Path.Combine(_directory, $"L0_{Guid.NewGuid():N}.sst");
        SSTableWriter.Write(sstPath, view.ActiveMemTable.GetSortedEntries(), _options.BloomFilterFalsePositiveRate, _options.Compression);

        var reader = SSTableReader.Open(sstPath, _blockCache);

        var levels = view.CloneLevels();
        if (levels.Count == 0) levels.Add([]);
        levels[0].Add(sstPath);

        var readers = view.CloneReaders();
        readers[sstPath] = reader;

        Volatile.Write(ref _view, new DbView(levels, readers, new MemTable()));

        ResetWal();

        _manifest.Write(levels);
        SignalCompaction();
    }

    /// <summary>
    /// 关闭并重建 WAL 文件。MemTable 已 flush 到 SST 后调用，清空 WAL 避免下次启动重复 replay。
    /// <para>
    /// Windows 上 FileStream.Dispose 后文件句柄释放可能有延迟（防病毒扫描、文件系统缓存），
    /// File.Delete 可能抛 IOException。此时改用截断文件内容代替删除；
    /// 截断也失败则保留原文件，WriteAheadLog.Open 以 Append 模式追加，下次 replay 幂等无碍。
    /// </para>
    /// </summary>
    private void ResetWal()
    {
        var walPath = Path.Combine(_directory, "wal.log");
        _wal?.Dispose();
        try
        {
            File.Delete(walPath);
        }
        catch (IOException)
        {
            try { using var fs = new FileStream(walPath, FileMode.Truncate, FileAccess.Write, FileShare.None); }
            catch { }
        }
        _wal = WriteAheadLog.Open(walPath, _options.WalSyncMode, _options.WalSyncIntervalMs);
    }

    /// <summary>
    /// Unconditionally flushes MemTable to SSTable on shutdown — no threshold check.
    /// <para>Ensures all in-memory writes survive across process restarts. Without this,
    /// unflushed data lives only in WAL; if a prior flush deleted/recreated the WAL,
    /// earlier writes are invisible to the next process.</para>
    /// </summary>
    private void FlushForShutdown()
    {
        if (_options.InMemory) return;

        var view = Volatile.Read(ref _view);
        if (view.ActiveMemTable.SizeBytes == 0) return;

        FlushActiveMemTable(view);
    }

    private void SignalCompaction()
    {
        if (_options.DisableCompaction) return;
        // Bounded capacity 1 with DropWrite: a pending signal already covers this flush.
        _compactionSignal.Writer.TryWrite(true);
    }

    private void CompactionLoop()
    {
        while (true)
        {
            bool signaled;
            try { signaled = _compactionSignal.Reader.WaitToReadAsync(_compactionCts.Token).AsTask().GetAwaiter().GetResult(); }  // style-allow: sync-block — 同步存储 API 契约: PithosDb 对外同步事务与压缩入口
            catch (OperationCanceledException) { return; }
            catch (ChannelClosedException) { return; }

            if (!signaled) return;

            while (_compactionSignal.Reader.TryRead(out _)) { }

            // Drain all pending work before sleeping again. A single
            // CompactIfNeeded pass may make a previously-under-limit level
            // go over its limit (L0→L1 can push L1 over its limit), so we
            // loop until no level needs compaction.
            try
            {
                while (_compactor.CompactIfNeeded()
                       && !_compactionCts.IsCancellationRequested) { }
            }
            catch { /* exceptions must not kill the compaction thread */ }
        }
    }

    /// <summary>
    /// Commits a finished compaction: publishes a new view (merged file added, sources
    /// removed) and rewrites the manifest before any source file is deleted. Runs as one
    /// writer-mailbox operation so it is serialised with flushes and WAL resets.
    /// </summary>
    /// <returns>The shared readers removed from the view; dispose them only after reads drain.</returns>
    internal List<SSTableReader> CommitCompaction(int level, IReadOnlyList<string> sources, string outPath, SSTableReader newReader)
    {
        List<SSTableReader> removed = [];
        EnqueueWriteCore(() =>
        {
            var view = Volatile.Read(ref _view);
            var levels = view.CloneLevels();
            while (levels.Count <= level + 1) levels.Add([]);
            levels[level + 1].Add(outPath);

            var readers = view.CloneReaders();
            readers[outPath] = newReader;

            foreach (var path in sources)
            {
                levels[level].Remove(path);
                if (readers.Remove(path, out var cached))
                    removed.Add(cached);
            }

            Volatile.Write(ref _view, new DbView(levels, readers, view.ActiveMemTable));
            _manifest.Write(levels);
        });
        return removed;
    }

    /// <summary>
    /// Blocks until every in-flight reader has left its read scope. The compactor calls
    /// this before disposing retired readers and deleting source SSTable files.
    /// </summary>
    internal void WaitForReadersToDrain()
    {
        var spinner = new SpinWait();
        while (Volatile.Read(ref _activeReads) != 0)
            spinner.SpinOnce();
    }

    // ── Recovery ──────────────────────────────────────────────────────────────

    private void RecoverFromWal()
    {
        var memTable = Volatile.Read(ref _view).ActiveMemTable;
        foreach (var (type, key, value) in WriteAheadLog.Replay(Path.Combine(_directory, "wal.log")))
        {
            if (type != WalEntryType.Put) {
                memTable.Delete(key);
                continue;
            }
            memTable.Put(key, value ?? throw new InvalidOperationException("WAL Put 条目缺少 value"));
        }
    }

    private void RecoverSSTables()
    {
        var log = _options.DiagnosticLog;
        var sstFiles = Directory.GetFiles(_directory, "*.sst");
        foreach (var path in sstFiles)
        {
            if (new FileInfo(path).Length == 0)
            {
                log?.Invoke($"[PithosDB.RecoverSSTables] deleting empty SST file: {path}");
                try { File.Delete(path); } catch { }
            }
        }

        var levels = new List<List<string>>();
        var readers = new Dictionary<string, SSTableReader>(StringComparer.Ordinal);

        if (_manifest.TryRead(out var manifestLevels))
        {
            for (int i = 0; i < manifestLevels.Count; i++)
            {
                while (levels.Count <= i) levels.Add([]);
                foreach (var path in manifestLevels[i])
                {
                    if (!File.Exists(path)) continue;
                    if (new FileInfo(path).Length == 0) continue;
                    levels[i].Add(path);
                    readers[path] = SSTableReader.Open(path, _blockCache);
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

                while (levels.Count <= level) levels.Add([]);
                levels[level].Add(path);
                readers[path] = SSTableReader.Open(path, _blockCache);
            }

            if (levels.Count > 0)
                _manifest.Write(levels);
        }

        var activeMemTable = Volatile.Read(ref _view).ActiveMemTable;
        Volatile.Write(ref _view, new DbView(levels, readers, activeMemTable));
    }

    // ── Stats ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a point-in-time <see cref="DbStats"/> snapshot: MemTable usage,
    /// per-level SSTable file counts and on-disk sizes, block cache occupancy,
    /// and WAL size. The snapshot is captured from the published view without
    /// blocking writers; the call is fast for typical level counts but does perform
    /// one <see cref="FileInfo"/> lookup per SSTable file.
    /// </summary>
    public DbStats GetStats()
    {
        using var scope = BeginRead();
        var view = scope.View;

        var fileCounts = view.Levels.Select(l => l.Count).ToList();
        var diskSizes  = view.Levels
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
            MemTableSizeBytes        = view.ActiveMemTable.SizeBytes,
            MemTableEntryCount       = view.ActiveMemTable.Count,
            LevelCount               = view.Levels.Count,
            FileCountPerLevel        = fileCounts,
            DiskSizeBytesPerLevel    = diskSizes,
            TotalSstFileCount        = fileCounts.Sum(),
            TotalSstDiskSizeBytes    = diskSizes.Sum(),
            BlockCacheCurrentSizeBytes = _blockCache?.CurrentSizeBytes ?? -1L,
            BlockCacheMaxSizeBytes     = _blockCache is not null ? _options.BlockCacheSizeBytes : -1L,
            WalSizeBytes             = walSize,
        };
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
    public Transaction BeginTransaction()
    {
        ThrowIfDisposed();
        return new Transaction(this, GetSnapshot());
    }

    // Validates the read set and applies the write batch atomically as one
    // writer-mailbox operation. Returns false (without applying any writes) on conflict.
    internal bool TryCommitTransaction(
        Snapshot snapshot,
        IReadOnlyCollection<byte[]> readKeys,
        WriteBatch batch) =>
        ExecuteWrite(() =>
        {
            var view = Volatile.Read(ref _view);
            foreach (var key in readKeys)
            {
                var current = ReadCurrentValue(view, key);
                snapshot.TryGet(key, out var snapVal);
                if (!BytesEqual(current, snapVal)) return false;
            }
            ApplyBatch(view, batch);
            return true;
        });

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
        using var scope = BeginRead();
        var view = scope.View;

        // Copy the MemTable into an independent sorted dictionary.
        var mem = new SortedDictionary<byte[], byte[]?>(ByteArrayComparer.Instance);
        foreach (var kv in view.ActiveMemTable.GetSortedEntries())
            mem[kv.Key] = kv.Value;

        // Capture the level structure and open dedicated readers for each SSTable.
        // These readers are independent of the shared cache so the snapshot remains
        // readable even after compaction recycles the shared cache entries.
        var levelPaths = view.Levels.Select(l => new List<string>(l)).ToList();
        var readers = new Dictionary<string, SSTableReader>(StringComparer.Ordinal);
        foreach (var path in levelPaths.SelectMany(l => l))
            readers[path] = SSTableReader.Open(path);

        return new Snapshot(mem, levelPaths, readers, _options);
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
    // The synchronous API blocks the calling thread on the writer mailbox. Each async
    // method offloads its synchronous counterpart to the thread pool via Task.Run so
    // callers' threads are freed while WAL fsyncs and SSTable reads are in progress —
    // the primary value for ASP.NET and other async-first hosts.

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
    /// Stops the background compaction and writer threads, flushes the active MemTable,
    /// closes the WAL, waits for in-flight readers to drain, then disposes all cached
    /// SSTable readers. Any compaction already in progress is allowed to finish first.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        // Stop compaction first — it may be mid-merge but will finish the current job
        // before seeing the cancellation.
        _compactionCts.Cancel();
        _compactionSignal.Writer.TryComplete();
        _compactionThread?.Join();
        _compactionCts.Dispose();

        // Drain whatever writes are still queued, then flush once more and stop the
        // writer thread. FlushForShutdown 失败不阻止后续资源释放（WAL/SST readers）。
        if (_writerThread is not null)
        {
            EnqueueWriteCore(FlushForShutdownSafe);
            _writeQueue.Writer.TryComplete();
            _writerThread.Join();
        }

        WaitForReadersToDrain();

        _wal?.Dispose();
        foreach (var reader in Volatile.Read(ref _view).Readers.Values)
        {
            try { reader.Dispose(); } catch { }
        }
    }

    private void FlushForShutdownSafe()
    {
        try { FlushForShutdown(); }
        catch (Exception ex) { _options.DiagnosticLog?.Invoke($"[PithosDB.Dispose] FlushForShutdown failed: {ex.Message}"); }
    }
}
