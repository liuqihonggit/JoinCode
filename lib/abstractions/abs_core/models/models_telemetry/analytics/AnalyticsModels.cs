namespace JoinCode.Abstractions.Models.Analytics;

public enum AnalyticsEventType
{
    [EnumValue("toolCall")] ToolCall,
    [EnumValue("toolSuccess")] ToolSuccess,
    [EnumValue("toolError")] ToolError,
    [EnumValue("agentStart")] AgentStart,
    [EnumValue("agentComplete")] AgentComplete,
    [EnumValue("userInteraction")] UserInteraction,
    [EnumValue("systemError")] SystemError,
    [EnumValue("performance")] Performance
}

public sealed record AnalyticsEvent
{
    public required string EventId { get; init; }
    public required AnalyticsEventType Type { get; init; }
    public required string Name { get; init; }
    public string? AgentName { get; init; }
    public string? SessionId { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public Dictionary<string, JsonElement> Data { get; init; } = new();
    public double? DurationMs { get; init; }
    public bool? IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed record AnalyticsExportData
{
    public required DateTime ExportTime { get; init; }
    public required DateTime? StartDate { get; init; }
    public required DateTime? EndDate { get; init; }
    public required int EventCount { get; init; }
    public required List<AnalyticsEvent> Events { get; init; }
}

public sealed record ToolUsageStatistics
{
    public required string ToolName { get; init; }
    public int CallCount { get; init; }
    public int SuccessCount { get; init; }
    public int ErrorCount { get; init; }
    public double SuccessRate => CallCount > 0 ? (double)SuccessCount / CallCount * 100 : 0;
    public double AverageDurationMs { get; init; }
    public DateTime? LastCallAt { get; init; }
}

public sealed record UsageStatisticsReport
{
    public int TotalEvents { get; init; }
    public int TotalToolCalls { get; init; }
    public double ToolSuccessRate { get; init; }
    public double AverageToolDurationMs { get; init; }
    public List<ToolUsageStatistics> TopTools { get; init; } = new();
    public Dictionary<DateTime, DailyStatistics> DailyStats { get; init; } = new();
    public double ErrorRate { get; init; }
}

public sealed record DailyStatistics
{
    public required DateTime Date { get; init; }
    public int EventCount { get; init; }
    public int ToolCalls { get; init; }
    public int ErrorCount { get; init; }
    public int ActiveAgents { get; init; }
}
