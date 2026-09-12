namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具干预类型 — 用户可强制禁用/降权/重定向工具
/// </summary>
public enum InterventionType
{
    [EnumValue("blacklist")] Blacklist,
    [EnumValue("downgrade")] Downgrade,
    [EnumValue("redirect")] Redirect,
}

/// <summary>
/// 工具干预规则 — 用户对工具的强制干预配置
/// </summary>
public sealed record InterventionRule
{
    public required InterventionType Type { get; init; }
    public required string Reason { get; init; }
    public DateTime? Expiry { get; init; }
    public DateTime StartTime { get; init; } = DateTime.UtcNow;
    public bool IsExpired => Expiry.HasValue && Expiry.Value < DateTime.UtcNow;
    public int? ScorePenalty { get; init; }
    public string? RedirectTo { get; init; }
}
