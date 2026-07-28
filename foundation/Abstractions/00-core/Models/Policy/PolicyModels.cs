
namespace JoinCode.Abstractions.Models.Policy;

public enum PolicyType
{
    [EnumValue("tool_usage_limit")] ToolUsageLimit = 0,
    [EnumValue("cost_limit")] CostLimit = 1,
    [EnumValue("rate_limit")] RateLimit = 2,
    [EnumValue("tool_restriction")] ToolRestriction = 3,
    [EnumValue("time_restriction")] TimeRestriction = 4
}

public enum PolicyAction
{
    [EnumValue("allow")] Allow = 0,
    [EnumValue("deny")] Deny = 1,
    [EnumValue("warn")] Warn = 2,
    [EnumValue("throttle")] Throttle = 3
}

public sealed class PolicyEvaluationResult
{
    public required string RuleId { get; init; }
    public required bool Allowed { get; init; }
    public required PolicyAction Action { get; init; }
    public string? Reason { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
    public int? RemainingLimit { get; init; }
    public TimeSpan? RetryAfter { get; init; }
}

public sealed class PolicyRule
{
    public required string RuleId { get; init; }
    public required string Name { get; init; }
    public required PolicyType Type { get; init; }
    public required PolicyAction Action { get; init; }
    public Dictionary<string, string>? Conditions { get; init; }
    public int? Limit { get; init; }
    public TimeSpan? Window { get; init; }
    public double? CostLimit { get; init; }
    public List<string>? RestrictedTools { get; init; }
    public bool Enabled { get; init; } = true;
    public int Priority { get; init; }
    public DateTime? UpdatedAt { get; init; }
}
