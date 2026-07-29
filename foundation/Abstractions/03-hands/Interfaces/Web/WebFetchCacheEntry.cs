namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// WebFetch缓存条目
/// </summary>
public sealed record WebFetchCacheEntry(
    string Content,
    string ContentType,
    int ContentBytes,
    int StatusCode,
    string StatusText,
    bool Truncated,
    string? PersistedPath = null,
    int PersistedSize = 0);
