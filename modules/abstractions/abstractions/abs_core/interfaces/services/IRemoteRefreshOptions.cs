
namespace JoinCode.Abstractions.Services;

public interface IRemoteRefreshOptions {
    /// <summary>获取 API 端点地址。</summary>
    string ApiEndpoint { get; }
    /// <summary>获取客户端密钥。</summary>
    string ClientKey { get; }
    /// <summary>获取刷新间隔。</summary>
    TimeSpan RefreshInterval { get; }
    /// <summary>获取缓存过期时间。</summary>
    TimeSpan CacheExpiration { get; }
    /// <summary>获取是否启用缓存。</summary>
    bool EnableCache { get; }
}