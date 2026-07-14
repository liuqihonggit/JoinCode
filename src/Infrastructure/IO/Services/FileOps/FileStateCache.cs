using JoinCode.Abstractions.Attributes;
using JoinCode.Abstractions.Interfaces;

namespace IO;

/// <summary>
/// LRU-based file state cache that tracks file reads for write-before-read validation.
/// Mirrors TS FileStateCache with normalized path keys and size limits.
/// </summary>
[Register]
public sealed partial class FileStateCache : IFileStateCache
{
    private const int DefaultMaxEntries = 100;
    private const long DefaultMaxSizeBytes = 25 * 1024 * 1024; // 25MB

    private readonly LruCache<string, FileReadState> _cache;

    public FileStateCache(int maxEntries = DefaultMaxEntries, long maxSizeBytes = DefaultMaxSizeBytes)
    {
        _cache = new LruCache<string, FileReadState>(
            maxEntries,
            maxSizeBytes,
            static state => Math.Max(1, state.Content.Length * sizeof(char)),
            StringComparer.OrdinalIgnoreCase);
    }

    public void RecordRead(string filePath, string content, long timestampMs, int? offset = null, int? limit = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var normalizedPath = NormalizePath(filePath);

        var state = new FileReadState
        {
            Content = content,
            TimestampMs = timestampMs,
            Offset = offset,
            Limit = limit,
            IsPartialView = offset.HasValue || limit.HasValue
        };

        _cache.Set(normalizedPath, state);
    }

    public bool HasBeenRead(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.ContainsKey(normalizedPath);
    }

    public long? GetReadTimestampMs(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.TryGet(normalizedPath, out var state) ? state.TimestampMs : null;
    }

    public string? GetReadContent(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.TryGet(normalizedPath, out var state) ? state.Content : null;
    }

    public FileReadState? GetReadState(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.TryGet(normalizedPath, out var state) ? state : null;
    }

    public void Invalidate(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        _cache.Remove(normalizedPath);
    }

    public void Clear() => _cache.Clear();

    public IFileStateCache Clone()
    {
        var cloned = new FileStateCache();
        foreach (var (key, state) in _cache.Entries())
        {
            cloned._cache.Set(key, new FileReadState
            {
                Content = state.Content,
                TimestampMs = state.TimestampMs,
                Offset = state.Offset,
                Limit = state.Limit,
                IsPartialView = state.IsPartialView
            });
        }
        return cloned;
    }

    private static string NormalizePath(string path)
    {
        return Path.GetFullPath(path).Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
    }
}
