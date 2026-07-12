namespace JoinCode.Reasoning.Evidence;

/// <summary>
/// 裁决结果
/// </summary>
public sealed class Verdict
{
    public required string ClaimId { get; init; }
    public required VerdictDecision Decision { get; init; }
    public string? Reason { get; init; }
    public int Confidence { get; init; }
}
