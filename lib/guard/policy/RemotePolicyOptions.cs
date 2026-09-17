
namespace Core.Policy;

/// <summary>
/// 远程策略选项 — 配置远程策略端点、刷新间隔、缓存与通知开关
/// </summary>
public sealed class RemotePolicyOptions : RemoteRefreshOptionsBase
{
    /// <summary>配置节名称</summary>
    public const string SectionName = "RemotePolicy";

    /// <summary>
    /// 初始化远程策略选项 — 刷新间隔默认 10 分钟,缓存过期默认 15 分钟
    /// </summary>
    public RemotePolicyOptions() : base(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(15)) { }

    /// <summary>是否启用变更通知 — 默认启用</summary>
    public bool EnableNotifications { get; set; } = true;
}
