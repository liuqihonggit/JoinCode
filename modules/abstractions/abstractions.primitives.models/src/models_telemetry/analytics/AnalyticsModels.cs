namespace JoinCode.Abstractions.Models.Analytics;

public enum AnalyticsEventType {
    [EnumValue("toolCall")] ToolCall,
    [EnumValue("toolSuccess")] ToolSuccess,
    [EnumValue("toolError")] ToolError,
    [EnumValue("agentStart")] AgentStart,
    [EnumValue("agentComplete")] AgentComplete,
    [EnumValue("userInteraction")] UserInteraction,
    [EnumValue("systemError")] SystemError,
    [EnumValue("performance")] Performance
}

public sealed record AnalyticsEvent {
    /// <summary>获取事件标识。</summary>
    public required string EventId { get; init; }
    /// <summary>获取事件类型。</summary>
    public required AnalyticsEventType Type { get; init; }
    /// <summary>获取事件名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取智能体名称。</summary>
    public string? AgentName { get; init; }
    /// <summary>获取会话标识。</summary>
    public string? SessionId { get; init; }
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    /// <summary>获取事件数据。</summary>
    public Dictionary<string, JsonElement> Data { get; init; } = new();
    /// <summary>获取持续时长(毫秒)。</summary>
    public double? DurationMs { get; init; }
    /// <summary>获取是否成功。</summary>
    public bool? IsSuccess { get; init; }
    /// <summary>获取错误消息。</summary>
    public string? ErrorMessage { get; init; }
}

public sealed record AnalyticsExportData {
    /// <summary>获取导出时间。</summary>
    public required DateTime ExportTime { get; init; }
    /// <summary>获取起始日期。</summary>
    public required DateTime? StartDate { get; init; }
    /// <summary>获取结束日期。</summary>
    public required DateTime? EndDate { get; init; }
    /// <summary>获取事件数量。</summary>
    public required int EventCount { get; init; }
    /// <summary>获取事件列表。</summary>
    public required List<AnalyticsEvent> Events { get; init; }
}

public sealed record ToolUsageStatistics {
    /// <summary>获取工具名称。</summary>
    public required string ToolName { get; init; }
    /// <summary>获取调用次数。</summary>
    public int CallCount { get; init; }
    /// <summary>获取成功次数。</summary>
    public int SuccessCount { get; init; }
    /// <summary>获取错误次数。</summary>
    public int ErrorCount { get; init; }
    /// <summary>获取成功率。</summary>
    public double SuccessRate => CallCount > 0 ? (double)SuccessCount / CallCount * 100 : 0;
    /// <summary>获取平均持续时长(毫秒)。</summary>
    public double AverageDurationMs { get; init; }
    /// <summary>获取最后调用时间。</summary>
    public DateTime? LastCallAt { get; init; }
}

public sealed record UsageStatisticsReport {
    /// <summary>获取总事件数。</summary>
    public int TotalEvents { get; init; }
    /// <summary>获取总工具调用数。</summary>
    public int TotalToolCalls { get; init; }
    /// <summary>获取工具成功率。</summary>
    public double ToolSuccessRate { get; init; }
    /// <summary>获取工具平均持续时长(毫秒)。</summary>
    public double AverageToolDurationMs { get; init; }
    /// <summary>获取热门工具列表。</summary>
    public List<ToolUsageStatistics> TopTools { get; init; } = new();
    /// <summary>获取按日统计字典。</summary>
    public Dictionary<DateTime, DailyStatistics> DailyStats { get; init; } = new();
    /// <summary>获取错误率。</summary>
    public double ErrorRate { get; init; }
}

public sealed record DailyStatistics {
    /// <summary>获取日期。</summary>
    public required DateTime Date { get; init; }
    /// <summary>获取事件数。</summary>
    public int EventCount { get; init; }
    /// <summary>获取工具调用数。</summary>
    public int ToolCalls { get; init; }
    /// <summary>获取错误数。</summary>
    public int ErrorCount { get; init; }
    /// <summary>获取活跃智能体数。</summary>
    public int ActiveAgents { get; init; }
}