
namespace JoinCode.Abstractions.Models.Policy;

public enum PolicyType {
    [EnumValue("tool_usage_limit")] ToolUsageLimit = 0,
    [EnumValue("cost_limit")] CostLimit = 1,
    [EnumValue("rate_limit")] RateLimit = 2,
    [EnumValue("tool_restriction")] ToolRestriction = 3,
    [EnumValue("time_restriction")] TimeRestriction = 4
}

public enum PolicyAction {
    [EnumValue("allow")] Allow = 0,
    [EnumValue("deny")] Deny = 1,
    [EnumValue("warn")] Warn = 2,
    [EnumValue("throttle")] Throttle = 3
}

public sealed class PolicyEvaluationResult {
    /// <summary>获取规则标识。</summary>
    public required string RuleId { get; init; }
    /// <summary>获取是否允许执行。</summary>
    public required bool Allowed { get; init; }
    /// <summary>获取策略动作。</summary>
    public required PolicyAction Action { get; init; }
    /// <summary>获取决策原因。</summary>
    public string? Reason { get; init; }
    /// <summary>获取元数据字典。</summary>
    public Dictionary<string, string> Metadata { get; init; } = [];
    /// <summary>获取剩余可用额度。</summary>
    public int? RemainingLimit { get; init; }
    /// <summary>获取重试等待时间。</summary>
    public TimeSpan? RetryAfter { get; init; }
}

public sealed class PolicyRule {
    /// <summary>获取规则标识。</summary>
    public required string RuleId { get; init; }
    /// <summary>获取规则名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取策略类型。</summary>
    public required PolicyType Type { get; init; }
    /// <summary>获取策略动作。</summary>
    public required PolicyAction Action { get; init; }
    /// <summary>获取条件字典。</summary>
    public Dictionary<string, string> Conditions { get; init; } = [];
    /// <summary>获取限额。</summary>
    public int? Limit { get; init; }
    /// <summary>获取时间窗口。</summary>
    public TimeSpan? Window { get; init; }
    /// <summary>获取成本限额。</summary>
    public double? CostLimit { get; init; }
    /// <summary>获取受限工具列表。</summary>
    public List<string> RestrictedTools { get; init; } = [];
    /// <summary>获取是否启用。</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>获取优先级。</summary>
    public int Priority { get; init; }
    /// <summary>获取更新时间。</summary>
    public DateTime? UpdatedAt { get; init; }
}
