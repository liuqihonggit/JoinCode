namespace PithosDB.Core;

/// <summary>
/// User-supplied callback invoked during compaction and at read time for every
/// surviving live entry. Returning <see langword="false"/> causes the entry to
/// be dropped — it becomes invisible to reads immediately and is physically
/// removed from merged SSTables during the next compaction that touches it.
/// <para>
/// The filter receives the decoded user value (TTL overhead stripped when
/// <see cref="PithosOptions.EnableTtl"/> is <see langword="true"/>).
/// </para>
/// <para>
/// Implementations must be thread-safe: <see cref="ShouldKeep"/> may be called
/// concurrently from multiple reader threads.
/// </para>
/// </summary>
public interface ICompactionFilter
{
    /// <summary>
    /// Returns <see langword="true"/> to keep the entry or <see langword="false"/>
    /// to drop it.
    /// </summary>
    bool ShouldKeep(ReadOnlySpan<byte> key, ReadOnlySpan<byte> value);
}
