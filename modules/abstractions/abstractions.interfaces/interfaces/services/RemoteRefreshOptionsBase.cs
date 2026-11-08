
namespace JoinCode.Abstractions.Services;

/// <summary>
/// 远程刷新选项抽象基类 — 统一 <see cref="IRemoteRefreshOptions"/> 的五个公共字段
/// (ApiEndpoint/ClientKey/RefreshInterval/CacheExpiration/EnableCache)。
/// 派生类通过构造函数注入各自的 RefreshInterval/CacheExpiration 默认值。
/// </summary>
public abstract class RemoteRefreshOptionsBase : IRemoteRefreshOptions {
    /// <summary>远程 API 端点地址</summary>
    public string ApiEndpoint { get; set; } = string.Empty;

    /// <summary>客户端密钥</summary>
    public string ClientKey { get; set; } = string.Empty;

    /// <summary>刷新间隔 — 由派生类构造函数注入默认值</summary>
    public TimeSpan RefreshInterval { get; set; }

    /// <summary>缓存过期时间 — 由派生类构造函数注入默认值</summary>
    public TimeSpan CacheExpiration { get; set; }

    /// <summary>是否启用缓存 — 默认启用</summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>
    /// 初始化基类,注入刷新间隔与缓存过期时间的默认值。
    /// </summary>
    /// <param name="refreshInterval">刷新间隔默认值</param>
    /// <param name="cacheExpiration">缓存过期时间默认值</param>
    protected RemoteRefreshOptionsBase(TimeSpan refreshInterval, TimeSpan cacheExpiration) {
        RefreshInterval = refreshInterval;
        CacheExpiration = cacheExpiration;
    }
}