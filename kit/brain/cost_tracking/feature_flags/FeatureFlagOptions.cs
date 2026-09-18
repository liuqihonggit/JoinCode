
namespace Core.CostTracking.FeatureFlags;

/// <summary>
/// 特性标志远程刷新配置选项
/// </summary>
public sealed class FeatureFlagOptions : RemoteRefreshOptionsBase
{
    /// <summary>
    /// 配置节名称
    /// </summary>
    public const string SectionName = "FeatureFlags";

    /// <summary>
    /// 初始化特性标志远程刷新配置选项 — 刷新间隔默认 5 分钟,缓存过期默认 10 分钟
    /// </summary>
    public FeatureFlagOptions() : base(TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)) { }
}
