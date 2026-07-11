
namespace Core.Policy;

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
