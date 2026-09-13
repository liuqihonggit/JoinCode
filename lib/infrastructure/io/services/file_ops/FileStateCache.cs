namespace IO;

/// <summary>
/// LRU-based file state cache that tracks file reads for write-before-read validation.
/// Mirrors TS FileStateCache with normalized path keys and size limits.
/// </summary>
[Register(typeof(IFileStateCache), ServiceLifetime.Singleton)]
public sealed partial class FileStateCache : ServiceEntity, IFileStateCache
{
    private const int DefaultMaxEntries = 100;
    private const long DefaultMaxSizeBytes = 25 * 1024 * 1024; // 25MB

    private readonly LruCache<string, FileReadState> _cache;

    /// <summary>
    /// 构造文件状态缓存
    /// </summary>
    /// <param name="maxEntries">最大缓存条目数</param>
    /// <param name="maxSizeBytes">最大缓存字节数</param>
    public FileStateCache(int maxEntries = DefaultMaxEntries, long maxSizeBytes = DefaultMaxSizeBytes)
    {
        _cache = new LruCache<string, FileReadState>(
            maxEntries,
            maxSizeBytes,
            static state => Math.Max(1, state.Content.Length * sizeof(char)),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 记录一次文件读取，缓存读取状态用于写入前校验
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <param name="content">读取到的内容</param>
    /// <param name="timestampMs">读取时间戳(毫秒)</param>
    /// <param name="offset">可选行偏移</param>
    /// <param name="limit">可选行限制</param>
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

    /// <summary>
    /// 判断指定文件是否已被读取过
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>已读取过返回 true，否则 false</returns>
    public bool HasBeenRead(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.ContainsKey(normalizedPath);
    }

    /// <summary>
    /// 获取指定文件的读取时间戳(毫秒)
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>读取时间戳，未读取过返回 null</returns>
    public long? GetReadTimestampMs(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.TryGetValue(normalizedPath, out var state) ? state.TimestampMs : null;
    }

    /// <summary>
    /// 获取指定文件的读取内容
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>读取内容，未读取过返回 null</returns>
    public string? GetReadContent(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.TryGetValue(normalizedPath, out var state) ? state.Content : null;
    }

    /// <summary>
    /// 获取指定文件的完整读取状态
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>读取状态，未读取过返回 null</returns>
    public FileReadState? GetReadState(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        return _cache.TryGetValue(normalizedPath, out var state) ? state : null;
    }

    /// <summary>
    /// 使指定文件的缓存失效
    /// </summary>
    /// <param name="filePath">文件路径</param>
    public void Invalidate(string filePath)
    {
        var normalizedPath = NormalizePath(filePath);
        _cache.Remove(normalizedPath);
    }

    /// <summary>
    /// 清空所有缓存条目
    /// </summary>
    public void Clear() => _cache.Clear();

    /// <summary>
    /// 克隆当前缓存，返回独立副本
    /// </summary>
    /// <returns>缓存副本</returns>
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
