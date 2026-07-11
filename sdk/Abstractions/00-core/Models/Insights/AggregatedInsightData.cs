namespace JoinCode.Abstractions.Insights;

/// <summary>
/// 聚合洞察数据 — 对齐 TS insights.ts AggregatedData
/// </summary>
public sealed class AggregatedInsightData
{
    public int TotalSessions { get; init; }
    public int TotalSessionsScanned { get; init; }
    public DateOnly? StartDate { get; init; }
    public DateOnly? EndDate { get; init; }
    public int TotalMessages { get; init; }
    public double TotalDurationHours { get; init; }
    public long TotalInputTokens { get; init; }
    public long TotalOutputTokens { get; init; }
    public int GitCommits { get; init; }
    public int GitPushes { get; init; }

    /// <summary>工具使用统计 (工具名 → 使用次数)</summary>
    public IReadOnlyDictionary<string, int> ToolCounts { get; init; } = new Dictionary<string, int>();

    /// <summary>语言分布 (语言名 → 出现次数)</summary>
    public IReadOnlyDictionary<string, int> Languages { get; init; } = new Dictionary<string, int>();

    /// <summary>项目分布 (项目路径 → 会话数)</summary>
    public IReadOnlyDictionary<string, int> Projects { get; init; } = new Dictionary<string, int>();

    public int TotalLinesAdded { get; init; }
    public int TotalLinesRemoved { get; init; }
    public int TotalFilesModified { get; init; }
    public int TotalInterruptions { get; init; }
    public int TotalToolErrors { get; init; }

    /// <summary>工具错误分类聚合</summary>
    public IReadOnlyDictionary<string, int> ToolErrorCategories { get; init; } = new Dictionary<string, int>();

    public int SessionsUsingTaskAgent { get; init; }
    public int SessionsUsingMcp { get; init; }
    public int SessionsUsingWebSearch { get; init; }
    public int SessionsUsingWebFetch { get; init; }

    /// <summary>活跃天数</summary>
    public int DaysActive { get; init; }

    /// <summary>每天平均消息数</summary>
    public double MessagesPerDay { get; init; }

    /// <summary>总估算成本</summary>
    public decimal TotalCostUsd { get; init; }

    /// <summary>当前连续使用天数 — 对齐 TS stats currentStreak</summary>
    public int CurrentStreak { get; init; }

    /// <summary>最长连续使用天数 — 对齐 TS stats longestStreak</summary>
    public int LongestStreak { get; init; }

    /// <summary>最长连续开始日期</summary>
    public DateOnly? LongestStreakStart { get; init; }

    /// <summary>最长连续结束日期</summary>
    public DateOnly? LongestStreakEnd { get; init; }

    /// <summary>最活跃日期 — 对齐 TS stats peakActivityDay</summary>
    public DateOnly? PeakActivityDay { get; init; }

    /// <summary>最活跃时段(0-23) — 对齐 TS stats peakActivityHour</summary>
    public int PeakActivityHour { get; init; }

    /// <summary>每日活动数据 — 对齐 TS stats dailyActivity</summary>
    public IReadOnlyList<DailyActivity> DailyActivities { get; init; } = Array.Empty<DailyActivity>();

    /// <summary>使用最多的模型 — 对齐 TS stats favorite model</summary>
    public string FavoriteModel { get; init; } = string.Empty;
}

/// <summary>
/// 每日活动数据 — 对齐 TS stats DailyActivity
/// </summary>
public sealed class DailyActivity
{
    public DateOnly Date { get; init; }
    public int MessageCount { get; init; }
    public int SessionCount { get; init; }
    public int ToolCallCount { get; init; }
}
