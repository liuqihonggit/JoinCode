namespace JoinCode.Abstractions.Interfaces;

public sealed record ToolActivity {
    /// <summary>获取工具名称。</summary>
    public required string ToolName { get; init; }
    /// <summary>获取活动描述。</summary>
    public string? ActivityDescription { get; init; }
    /// <summary>获取是否为搜索活动。</summary>
    public bool IsSearch { get; init; }
    /// <summary>获取是否为读取活动。</summary>
    public bool IsRead { get; init; }
    /// <summary>获取工具输入参数。</summary>
    public Dictionary<string, string>? Input { get; init; }
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

public sealed record AgentProgress {
    /// <summary>获取工具使用次数。</summary>
    public required int ToolUseCount { get; init; }
    /// <summary>获取 Token 总数。</summary>
    public required int TokenCount { get; init; }
    /// <summary>获取最近一次工具活动。</summary>
    public ToolActivity? LastActivity { get; init; }
    /// <summary>获取最近活动集合。</summary>
    public IEnumerable<ToolActivity>? RecentActivities { get; init; }
    /// <summary>获取进度摘要。</summary>
    public string? Summary { get; init; }
}
