
namespace Core.Configuration.Remote;

/// <summary>
/// 远程设置刷新选项 — 配置远程托管设置的拉取端点、刷新间隔与缓存策略
/// </summary>
public sealed class RemoteSettingsOptions : IRemoteRefreshOptions
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "RemoteSettings";

    /// <summary>
    /// 远程 API 端点地址
    /// </summary>
    public string ApiEndpoint { get; set; } = string.Empty;
    /// <summary>
    /// 客户端密钥
    /// </summary>
    public string ClientKey { get; set; } = string.Empty;
    /// <summary>
    /// 刷新间隔
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(15);
    /// <summary>
    /// 缓存过期时间
    /// </summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(20);
    /// <summary>
    /// 是否启用缓存
    /// </summary>
    public bool EnableCache { get; set; } = true;
    /// <summary>
    /// 是否启用通知
    /// </summary>
    public bool EnableNotifications { get; set; } = true;
    /// <summary>
    /// 是否与本地设置合并
    /// </summary>
    public bool MergeWithLocal { get; set; } = true;
}
