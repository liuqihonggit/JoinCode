namespace PithosDB.Core;

/// <summary>
/// A point-in-time snapshot of database runtime statistics returned by
/// <see cref="PithosDb.GetStats"/>. All values reflect the state at the
/// moment the call was made.
/// </summary>
public sealed class DbStats
{
    // ── MemTable ──────────────────────────────────────────────────────────

    /// <summary>
    /// Approximate bytes currently buffered in the MemTable (not yet flushed to disk).
    /// </summary>
    public long MemTableSizeBytes { get; init; }

    /// <summary>
    /// Number of entries in the MemTable, including tombstones from recent deletes.
    /// </summary>
    public int MemTableEntryCount { get; init; }

    // ── SSTables ──────────────────────────────────────────────────────────

    /// <summary>
    /// Number of active SSTable levels. Zero when all data is still in the MemTable
    /// (no flush has occurred) or when the database is in in-memory mode.
    /// </summary>
    public int LevelCount { get; init; }

    /// <summary>
    /// Number of SSTable files in each level, indexed by level number (L0 first).
    /// </summary>
    public IReadOnlyList<int> FileCountPerLevel { get; init; } = [];

    /// <summary>
    /// On-disk bytes consumed by SSTable files in each level, indexed by level number.
    /// </summary>
    public IReadOnlyList<long> DiskSizeBytesPerLevel { get; init; } = [];

    /// <summary>Total number of SSTable files across all levels.</summary>
    public int TotalSstFileCount { get; init; }

    /// <summary>Total on-disk bytes consumed by all SSTable files.</summary>
    public long TotalSstDiskSizeBytes { get; init; }

    // ── Block cache ───────────────────────────────────────────────────────

    /// <summary>
    /// Bytes currently stored in the block cache, or <c>-1</c> if no cache is configured.
    /// </summary>
    public long BlockCacheCurrentSizeBytes { get; init; }

    /// <summary>
    /// Maximum block cache capacity in bytes, or <c>-1</c> if no cache is configured.
    /// </summary>
    public long BlockCacheMaxSizeBytes { get; init; }

    // ── WAL ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Current WAL file size in bytes, or <c>-1</c> for in-memory databases.
    /// The WAL is truncated to zero after each MemTable flush.
    /// </summary>
    public long WalSizeBytes { get; init; }
}
