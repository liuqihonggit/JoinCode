namespace JoinCode.Abstractions.Models;

/// <summary>
/// 实体回收器配置
/// </summary>
public sealed class EntityReaperConfig
{
    /// <summary>
    /// 扫描间隔 — 默认 60 秒
    /// </summary>
    public TimeSpan ScanInterval { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 泄漏告警阈值 — 超过此时间未 Dispose 的 Entity 视为疑似泄漏
    /// </summary>
    public TimeSpan MaxAgeBeforeLeakWarning { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// 是否启用自动回收 — false 时只告警不回收
    /// </summary>
    public bool EnableAutoReclaim { get; init; } = true;

    /// <summary>
    /// 是否启用泄漏检测
    /// </summary>
    public bool EnableLeakDetection { get; init; } = true;
}
