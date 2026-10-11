namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具干预类型 — 用户可强制禁用/降权/重定向工具
/// </summary>
public enum InterventionType {
    [EnumValue("blacklist")] Blacklist,
    [EnumValue("downgrade")] Downgrade,
    [EnumValue("redirect")] Redirect,
}

/// <summary>
/// 工具干预规则 — 用户对工具的强制干预配置
/// </summary>
public sealed record InterventionRule {
    /// <summary>获取干预类型。</summary>
    public required InterventionType Type { get; init; }
    /// <summary>获取干预原因。</summary>
    public required string Reason { get; init; }
    /// <summary>获取干预过期时间。</summary>
    public DateTime? Expiry { get; init; }
    /// <summary>获取干预开始时间。</summary>
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    /// <summary>获取是否已过期。</summary>
    public bool IsExpired => Expiry.HasValue && Expiry.Value < DateTime.UtcNow;
    /// <summary>获取评分惩罚值。</summary>
    public int? ScorePenalty { get; init; }
    /// <summary>获取重定向目标工具名称。</summary>
    public string? RedirectTo { get; init; }
}
