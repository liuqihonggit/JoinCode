namespace Core.Configuration;

/// <summary>
/// 更新源配置 — settings.json 的 update 节点
/// 控制自动更新行为：更新源类型、清单地址、通道、是否自动更新
/// > ADR: 0064
/// </summary>
public sealed class UpdateSourceConfig
{
    /// <summary>
    /// 更新源类型 — static/api/github-mirror/local，默认 static
    /// </summary>
    [JsonPropertyName("sourceType")]
    public string SourceType { get; init; } = "static";

    /// <summary>
    /// 清单地址 — HTTP URL / 本地路径 / UNC 路径
    /// null 时使用 JccEndpointsResolver.UpdateManifestUrl（环境变量或默认值）
    /// </summary>
    [JsonPropertyName("manifestUrl")]
    public string? ManifestUrl { get; init; }

    /// <summary>
    /// 是否自动下载安装 — false=仅通知有更新，true=自动下载+替换+提示重启
    /// </summary>
    [JsonPropertyName("autoUpdate")]
    public bool AutoUpdate { get; init; }

    /// <summary>
    /// 启动时是否检查更新
    /// </summary>
    [JsonPropertyName("checkOnStartup")]
    public bool CheckOnStartup { get; init; } = true;

    /// <summary>
    /// 检查间隔（小时）— 24 表示每 24 小时最多检查一次
    /// </summary>
    [JsonPropertyName("checkIntervalHours")]
    public int CheckIntervalHours { get; init; } = 24;

    /// <summary>
    /// 更新通道 — stable/beta/canary
    /// </summary>
    [JsonPropertyName("channel")]
    public string Channel { get; init; } = "stable";

    /// <summary>
    /// 解析为 UpdateSourceType 枚举
    /// </summary>
    public UpdateSourceType GetSourceType() =>
        UpdateSourceTypeExtensions.FromValue(SourceType) ?? UpdateSourceType.Static;

    /// <summary>
    /// 获取有效的清单地址 — 优先配置值，回退到 JccEndpointsResolver
    /// </summary>
    public string GetManifestUrl() =>
        !string.IsNullOrEmpty(ManifestUrl) ? ManifestUrl! : JccEndpointsResolver.UpdateManifestUrl;

    /// <summary>
    /// 获取有效的通道 — 优先配置值，回退到 JccEndpointsResolver
    /// </summary>
    public string GetChannel() =>
        !string.IsNullOrEmpty(Channel) ? Channel : JccEndpointsResolver.UpdateChannel;
}
