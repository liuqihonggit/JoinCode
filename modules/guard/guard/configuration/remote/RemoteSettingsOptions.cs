
namespace Core.Configuration.Remote;

/// <summary>
/// 远程设置刷新选项 — 配置远程托管设置的拉取端点、刷新间隔与缓存策略
/// </summary>
public sealed class RemoteSettingsOptions : RemoteRefreshOptionsBase {
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "RemoteSettings";

    /// <summary>
    /// 初始化远程设置刷新选项 — 刷新间隔默认 15 分钟,缓存过期默认 20 分钟
    /// </summary>
    public RemoteSettingsOptions() : base(TimeSpan.FromMinutes(15), TimeSpan.FromMinutes(20)) { }

    /// <summary>
    /// 是否启用通知
    /// </summary>
    public bool EnableNotifications { get; set; } = true;
    /// <summary>
    /// 是否与本地设置合并
    /// </summary>
    public bool MergeWithLocal { get; set; } = true;
}