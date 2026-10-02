using PithosDB.Core.Core;

namespace PithosDB.Core;

/// <summary>
/// Controls when the WAL is flushed to the OS write buffer after each write.
/// </summary>
public enum WalSyncMode
{
    /// <summary>
    /// Flush to the OS write buffer after every WAL record (default). Protects
    /// against .NET process crash; data is safe as soon as the write returns.
    /// </summary>
    Full,

    /// <summary>
    /// Flush on a background timer every <see cref="PithosOptions.WalSyncIntervalMs"/>
    /// milliseconds. A process crash can lose at most one interval's worth of writes.
    /// Substantially higher write throughput for workloads with many small writes.
    /// </summary>
    Periodic,

    /// <summary>
    /// Never flush explicitly; the OS decides when to write buffered data to disk.
    /// Maximum throughput; a process or OS crash may lose an unbounded number of
    /// recent writes. Appropriate for caches or ephemeral data.
    /// </summary>
    None,
}

/// <summary>SSTable block compression algorithm.</summary>
public enum CompressionKind
{
    /// <summary>No compression. Best for already-compressed data or latency-critical workloads.</summary>
    None,
    /// <summary>
    /// LZ4 block compression. Extremely fast decompression (~5 GB/s), modest CPU cost on writes.
    /// Recommended for most workloads — reduces I/O without measurably increasing read latency.
    /// </summary>
    Lz4,
}

/// <summary>Block cache eviction policy.</summary>
public enum BlockCacheKind
{
    /// <summary>Least-Recently-Used. Good general-purpose default.</summary>
    Lru,
    /// <summary>
    /// S3-FIFO (Simple, Scalable SLFU with three FIFOs, SOSP 2023).
    /// New blocks enter a small probation queue; only blocks accessed more than
    /// once are promoted to the main queue. Provides better scan resistance than
    /// LRU and lower per-hit overhead (no structural mutation on cache hit).
    /// </summary>
    S3Fifo,
}

/// <summary>
/// Tuning options for <see cref="PithosDb"/>. All properties have sensible
/// defaults; only override what you need to change.
/// </summary>
public sealed class PithosOptions
{
    /// <summary>
    /// In-memory MemTable size in bytes at which a flush to an L0 SSTable is
    /// triggered. Larger values reduce write amplification but increase memory
    /// usage and recovery time. Default: 4 MB.
    /// </summary>
    public long MemTableSizeThreshold { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// Target false positive probability for per-SSTable bloom filters (exclusive
    /// range 0–1). Lower values cut unnecessary disk reads but increase the
    /// in-memory and on-disk size of each filter. Default: 0.01 (1%).
    /// </summary>
    public double BloomFilterFalsePositiveRate { get; init; } = 0.01;

    /// <summary>
    /// Total number of compaction levels. Default: 7.
    /// </summary>
    public int LevelCount { get; init; } = 7;

    /// <summary>
    /// Maximum number of SSTable files allowed at L0 before a compaction into
    /// L1 is triggered. Default: 10.
    /// </summary>
    public int LevelZeroFileCountLimit { get; init; } = 10;

    /// <summary>
    /// Multiplier applied to the file-count limit at each successive level.
    /// With a limit of 10 and a multiplier of 10: L0=10, L1=100, L2=1 000, …
    /// Default: 10.
    /// </summary>
    public int LevelSizeMultiplier { get; init; } = 10;

    /// <summary>
    /// Maximum number of bytes the shared block cache may hold. Frequently-read
    /// SSTable blocks are kept in memory until evicted. Set to 0 to disable.
    /// Default: 8 MB.
    /// </summary>
    public long BlockCacheSizeBytes { get; init; } = 8 * 1024 * 1024;

    /// <summary>
    /// Eviction policy for the block cache. Default: <see cref="BlockCacheKind.Lru"/>.
    /// Ignored when <see cref="BlockCacheSizeBytes"/> is 0.
    /// </summary>
    public BlockCacheKind BlockCacheKind { get; init; } = BlockCacheKind.Lru;

    /// <summary>
    /// Block compression algorithm applied when writing SSTables. Compressed blocks are
    /// transparently decompressed on read. Default: <see cref="CompressionKind.None"/>.
    /// <para>
    /// All SSTables in a database must be written with the same algorithm. Changing
    /// <see cref="Compression"/> on an existing database will produce unreadable files.
    /// </para>
    /// </summary>
    public CompressionKind Compression { get; init; } = CompressionKind.None;

    /// <summary>
    /// Enables per-entry TTL support. When <see langword="true"/>, all values are stored
    /// with a 1-byte encoding header; <see cref="PithosDb.Put(byte[], byte[], TimeSpan)"/>
    /// becomes available and expired entries are automatically hidden at read time and
    /// removed during compaction.
    /// <para>
    /// <b>Important:</b> this option must be set consistently across all opens of the same
    /// database. Enabling or disabling it on an existing database will corrupt reads.
    /// Default: <see langword="false"/>.
    /// </para>
    /// </summary>
    public bool EnableTtl { get; init; } = false;

    /// <summary>
    /// Optional filter applied to every live entry at read time and during compaction.
    /// Entries for which <see cref="ICompactionFilter.ShouldKeep"/> returns
    /// <see langword="false"/> are immediately invisible and are physically removed from
    /// merged SSTables on the next compaction. The filter receives the decoded user value
    /// (TTL overhead already stripped when <see cref="EnableTtl"/> is
    /// <see langword="true"/>).
    /// <para>
    /// The implementation must be thread-safe. Default: <see langword="null"/> (no filter).
    /// </para>
    /// </summary>
    public ICompactionFilter? CompactionFilter { get; init; } = null;

    /// <summary>
    /// Controls when WAL records are flushed to the OS write buffer.
    /// Default: <see cref="WalSyncMode.Full"/> (flush after every write).
    /// Ignored when <see cref="InMemory"/> is <see langword="true"/>.
    /// </summary>
    public WalSyncMode WalSyncMode { get; init; } = WalSyncMode.Full;

    /// <summary>
    /// Flush interval in milliseconds used when <see cref="WalSyncMode"/> is
    /// <see cref="WalSyncMode.Periodic"/>. Must be greater than zero. Default: 200 ms.
    /// </summary>
    public int WalSyncIntervalMs { get; init; } = 200;

    /// <summary>
    /// When <see langword="true"/>, all data lives exclusively in memory. No WAL is written,
    /// no SSTables are flushed to disk, and no state is recovered on open. Data is lost when
    /// the instance is disposed. Useful for testing, caching, and ephemeral workloads.
    /// Default: <see langword="false"/>.
    /// </summary>
    public bool InMemory { get; init; } = false;

    /// <summary>Default options — equivalent to <c>new PithosOptions()</c>.</summary>
    public static readonly PithosOptions Default = new();

    /// <summary>
    /// Throws <see cref="ArgumentOutOfRangeException"/> if any property is outside
    /// its valid range.
    /// </summary>
    internal void Validate()
    {
        if (MemTableSizeThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(MemTableSizeThreshold), "Must be greater than zero.");

        if (BloomFilterFalsePositiveRate is <= 0 or >= 1)
            throw new ArgumentOutOfRangeException(nameof(BloomFilterFalsePositiveRate), "Must be between 0 and 1 exclusive.");

        if (LevelCount < 1)
            throw new ArgumentOutOfRangeException(nameof(LevelCount), "Must be at least 1.");

        if (LevelZeroFileCountLimit < 1)
            throw new ArgumentOutOfRangeException(nameof(LevelZeroFileCountLimit), "Must be at least 1.");

        if (LevelSizeMultiplier < 2)
            throw new ArgumentOutOfRangeException(nameof(LevelSizeMultiplier), "Must be at least 2.");

        if (BlockCacheSizeBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(BlockCacheSizeBytes), "Must be zero or greater.");

        if (WalSyncMode == WalSyncMode.Periodic && WalSyncIntervalMs <= 0)
            throw new ArgumentOutOfRangeException(nameof(WalSyncIntervalMs), "Must be greater than zero when WalSyncMode is Periodic.");
    }
}
