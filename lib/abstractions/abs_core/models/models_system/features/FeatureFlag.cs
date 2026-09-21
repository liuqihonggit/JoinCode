namespace JoinCode.Abstractions.Models.Features;

/// <summary>功能开关。</summary>
public sealed class FeatureFlag {
    /// <summary>获取功能开关键名。</summary>
    public required string Key { get; init; }
    /// <summary>获取是否启用。</summary>
    public bool Enabled { get; init; }
    /// <summary>获取灰度发布百分比。</summary>
    public double RolloutPercentage { get; init; }
    /// <summary>获取目标规则字典。</summary>
    public Dictionary<string, string> TargetingRules { get; init; } = [];
    /// <summary>获取默认值。</summary>
    public object? DefaultValue { get; init; }
    /// <summary>获取最后更新时间。</summary>
    public DateTime? UpdatedAt { get; init; }
}
