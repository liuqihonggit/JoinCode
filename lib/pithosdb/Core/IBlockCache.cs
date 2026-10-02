using System.Diagnostics.CodeAnalysis;

namespace PithosDB.Core.Core;

/// <summary>
/// Shared block cache that sits between SSTable readers and the disk.
/// Implementations are responsible for their own thread safety.
/// </summary>
public interface IBlockCache
{
    /// <summary>
    /// Returns the cached block data for (<paramref name="path"/>, <paramref name="offset"/>),
    /// or <see langword="false"/> if not present.
    /// </summary>
    bool TryGet(string path, long offset, [NotNullWhen(true)] out byte[]? data);

    /// <summary>
    /// Stores <paramref name="data"/> for (<paramref name="path"/>, <paramref name="offset"/>).
    /// No-ops if the key is already cached.
    /// </summary>
    void Put(string path, long offset, byte[] data);

    /// <summary>
    /// Evicts all cached blocks belonging to <paramref name="path"/>. Call this
    /// before deleting the underlying SSTable file during compaction.
    /// </summary>
    void EvictFile(string path);

    /// <summary>Current number of bytes stored in the cache.</summary>
    long CurrentSizeBytes { get; }
}
