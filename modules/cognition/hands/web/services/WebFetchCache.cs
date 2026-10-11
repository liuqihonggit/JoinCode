namespace Services.Web;

/// <summary>
/// WebFetch专用LRU缓存，15分钟TTL，50MB大小上限
/// 对齐TS版 URL_CACHE + DOMAIN_CHECK_CACHE
/// </summary>
[Register(typeof(IWebFetchCache), ServiceLifetime.Singleton)]
public sealed partial class WebFetchCache : ServiceEntity, IWebFetchCache, IDisposable {
    private const int MaxCacheSizeBytes = 50 * 1024 * 1024; // 50MB
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan DomainCheckTtl = TimeSpan.FromMinutes(5);
    private const int MaxDomainCheckEntries = 128;

    private readonly MemoryCache _urlCache;
    private readonly MemoryCache _domainCheckCache;
    private readonly ILogger<WebFetchCache>? _logger;
    private bool _disposed;

    /// <summary>
    /// 初始化 <see cref="WebFetchCache"/> 实例，创建 URL 缓存与域名预检缓存。
    /// </summary>
    /// <param name="logger">可选的日志记录器。</param>
    public WebFetchCache(ILogger<WebFetchCache>? logger = null) {
        _logger = logger;
        _urlCache = new MemoryCache(new MemoryCacheOptions {
            SizeLimit = MaxCacheSizeBytes,
            CompactionPercentage = 0.25,
            ExpirationScanFrequency = TimeSpan.FromMinutes(5)
        });
        _domainCheckCache = new MemoryCache(new MemoryCacheOptions {
            SizeLimit = MaxDomainCheckEntries,
            CompactionPercentage = 0.25,
            ExpirationScanFrequency = TimeSpan.FromMinutes(2)
        });
    }

    /// <summary>
    /// 尝试获取URL缓存内容
    /// </summary>
    /// <param name="url">待查询的 URL。</param>
    /// <returns>命中时返回缓存条目，未命中返回 null。</returns>
    public WebFetchCacheEntry? TryGet(string url) {
        if (_urlCache.TryGetValue(url, out WebFetchCacheEntry? entry)) {
            _logger?.LogDebug("WebFetch缓存命中: {Url}", url);
            return entry;
        }
        return null;
    }

    /// <summary>
    /// 设置URL缓存内容
    /// </summary>
    /// <param name="url">缓存键的 URL。</param>
    /// <param name="entry">待缓存的抓取结果条目。</param>
    public void Set(string url, WebFetchCacheEntry entry) {
        // LRU-cache要求正整数size，空响应clamp到1
        var size = Math.Max(1, entry.ContentBytes);
        _urlCache.Set(url, entry, new MemoryCacheEntryOptions {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = size
        });
        _logger?.LogDebug("WebFetch缓存写入: {Url}, Size={Size}", url, size);
    }

    /// <summary>
    /// 检查域名是否已通过黑名单预检（缓存命中=allowed）
    /// </summary>
    /// <param name="domain">待检查的域名。</param>
    /// <returns>缓存命中返回 true，否则返回 false。</returns>
    public bool IsDomainCheckCached(string domain) {
        return _domainCheckCache.TryGetValue(domain, out _);
    }

    /// <summary>
    /// 缓存域名预检通过结果
    /// </summary>
    /// <param name="domain">待缓存的域名。</param>
    public void CacheDomainCheck(string domain) {
        _domainCheckCache.Set(domain, true, new MemoryCacheEntryOptions {
            AbsoluteExpirationRelativeToNow = DomainCheckTtl,
            Size = 1
        });
    }

    /// <summary>
    /// 清理全部缓存（会话清理时调用）
    /// </summary>
    public void Clear() {
        _urlCache.Clear();
        _domainCheckCache.Clear();
        _logger?.LogInformation("WebFetch缓存已清空");
    }

    /// <summary>
    /// 释放 URL 缓存与域名预检缓存资源。
    /// </summary>
    public override void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _urlCache.Dispose();
        _domainCheckCache.Dispose();
        base.Dispose();
    }
}