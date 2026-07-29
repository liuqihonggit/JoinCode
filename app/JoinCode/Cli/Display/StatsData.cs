namespace JoinCode.Cli;

/// <summary>
/// 统计数据模型 — 对齐 TS Stats 组件数据模型
/// </summary>
public sealed class StatsData
{
    public int TotalSessions { get; set; }
    public int TotalInputTokens { get; set; }
    public int TotalOutputTokens { get; set; }
    public decimal TotalCostUsd { get; set; }
    public int ActiveDays { get; set; }
    public int LongestSessionMinutes { get; set; }
    public List<ModelStats> ModelBreakdown { get; } = [];
    public List<DailyUsage> DailyUsage { get; } = [];
    public DateTime? DateRangeStart { get; set; }
    public DateTime? DateRangeEnd { get; set; }

    public int TotalTokens => TotalInputTokens + TotalOutputTokens;
}

/// <summary>
/// 模型统计
/// </summary>
public sealed class ModelStats
{
    public string Model { get; }
    public int InputTokens { get; }
    public int OutputTokens { get; }
    public decimal CostUsd { get; }

    public ModelStats(string model, int inputTokens, int outputTokens, decimal costUsd)
    {
        Model = model;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        CostUsd = costUsd;
    }

    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// 每日使用量
/// </summary>
public sealed class DailyUsage
{
    public required DateTime Date { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public decimal CostUsd { get; init; }
    public int TotalTokens => InputTokens + OutputTokens;
}

/// <summary>
/// 统计 Tab 类型
/// </summary>
public enum StatsTab
{
    Overview,
    Models,
    Daily
}
