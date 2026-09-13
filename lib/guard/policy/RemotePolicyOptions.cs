
namespace Core.Policy;

/// <summary>
/// 远程策略选项 — 配置远程策略端点、刷新间隔、缓存与通知开关
/// </summary>
public sealed class RemotePolicyOptions : IRemoteRefreshOptions
{
    /// <summary>配置节名称</summary>
    public const string SectionName = "RemotePolicy";

    /// <summary>远程策略 API 端点地址</summary>
    public string ApiEndpoint { get; set; } = string.Empty;

    /// <summary>客户端密钥</summary>
    public string ClientKey { get; set; } = string.Empty;

    /// <summary>刷新间隔 — 默认 10 分钟</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>缓存过期时间 — 默认 15 分钟</summary>
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>是否启用缓存 — 默认启用</summary>
    public bool EnableCache { get; set; } = true;

    /// <summary>是否启用变更通知 — 默认启用</summary>
    public bool EnableNotifications { get; set; } = true;
}
