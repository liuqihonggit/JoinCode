namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// WebFetch专用LRU缓存接口，15分钟TTL，50MB大小上限
/// </summary>
public interface IWebFetchCache
{
    /// <summary>
    /// 尝试获取URL缓存内容
    /// </summary>
    WebFetchCacheEntry? TryGet(string url);

    /// <summary>
    /// 设置URL缓存内容
    /// </summary>
    void Set(string url, WebFetchCacheEntry entry);

    /// <summary>
    /// 检查域名是否已通过黑名单预检（缓存命中=allowed）
    /// </summary>
    bool IsDomainCheckCached(string domain);

    /// <summary>
    /// 缓存域名预检通过结果
    /// </summary>
    void CacheDomainCheck(string domain);

    /// <summary>
    /// 清理全部缓存（会话清理时调用）
    /// </summary>
    void Clear();
}
