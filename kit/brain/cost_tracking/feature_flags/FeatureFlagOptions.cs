
namespace Core.CostTracking.FeatureFlags;

/// <summary>
/// 特性标志远程刷新配置选项
/// </summary>
public sealed class FeatureFlagOptions : IRemoteRefreshOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "FeatureFlags";

    /// <summary>
    /// 特性标志 API 端点地址
    /// </summary>
    public string ApiEndpoint { get; set; } = string.Empty;

    /// <summary>
    /// 客户端密钥
    /// </summary>
    public string ClientKey { get; set; } = string.Empty;

    /// <summary>
    /// 远程刷新间隔时间
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 缓存过期时间
    /// </summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// 是否启用缓存
    /// </summary>
    public bool EnableCache { get; set; } = true;
}
