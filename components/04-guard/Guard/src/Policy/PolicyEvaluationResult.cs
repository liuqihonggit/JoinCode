
namespace Core.Policy;

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
