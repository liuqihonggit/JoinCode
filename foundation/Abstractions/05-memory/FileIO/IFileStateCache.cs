namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Tracks file read state for write-before-read validation.
/// Mirrors TS FileStateCache - ensures files are read before being written/edited.
/// </summary>
public interface IFileStateCache
{
    /// <summary>
    /// Record that a file has been read at the given timestamp.
    /// </summary>
    void RecordRead(string filePath, string content, long timestampMs, int? offset = null, int? limit = null);

    /// <summary>
    /// Check if a file has been read.
    /// </summary>
    bool HasBeenRead(string filePath);

    /// <summary>
    /// Get the last read timestamp for a file (milliseconds since epoch).
    /// Returns null if the file has not been read.
    /// </summary>
    long? GetReadTimestampMs(string filePath);

    /// <summary>
    /// Get the content that was read for a file.
    /// Returns null if the file has not been read.
    /// </summary>
    string? GetReadContent(string filePath);

    /// <summary>
    /// Get the full read state for a file.
    /// Returns null if the file has not been read.
    /// 对齐 TS: readFileState.get() — 用于去重判断
    /// </summary>
    FileReadState? GetReadState(string filePath);

    /// <summary>
    /// Clear the read state for a specific file.
    /// </summary>
    void Invalidate(string filePath);

    /// <summary>
    /// Clear all read states.
    /// </summary>
    void Clear();

    /// <summary>
    /// Deep clone this cache instance — children get independent state.
    /// Mirrors TS: cloneFileStateCache(parentCache).
    /// </summary>
    IFileStateCache Clone();
}

/// <summary>
/// Represents the state of a file that has been read.
/// </summary>
public sealed record FileReadState
{
    public required string Content { get; init; }
    public required long TimestampMs { get; init; }
    public int? Offset { get; init; }
    public int? Limit { get; init; }
    public bool IsPartialView { get; init; }
}
