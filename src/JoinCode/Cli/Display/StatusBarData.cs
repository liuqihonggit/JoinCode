namespace JoinCode.Cli;

/// <summary>
/// 状态栏数据 — 对齐 TS StatusBar 组件数据模型
/// </summary>
public sealed class StatusBarData
{
    public string Model { get; set; } = "";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int ContextWindowSize { get; set; }
    public decimal TotalCostUsd { get; set; }
    public EffortLevel EffortLevel { get; set; } = EffortLevel.Auto;
    public string? SessionName { get; set; }
    public string? WorktreeSession { get; set; }
    public double? RateLimitUsedPercentage { get; set; }
    public DateTime? RateLimitResetsAt { get; set; }
    public PermissionMode PermissionMode { get; set; } = PermissionMode.Default;

    public int TotalTokens => InputTokens + OutputTokens;

    public double UsedPercentage => ContextWindowSize > 0
        ? (double)TotalTokens / ContextWindowSize * 100
        : 0;
}
