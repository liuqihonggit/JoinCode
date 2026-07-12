namespace JoinCode.Reasoning.Engine;

/// <summary>
/// 推理引擎状态摘要
/// </summary>
public sealed class ReasoningSummary
{
    public int TotalAssumptions { get; init; }
    public int TotalVerified { get; init; }
    public int TotalFacts { get; init; }
    public int TotalRejected { get; init; }
    public int TotalPendingEvidence { get; init; }
    public int TotalEvidence { get; init; }
    public DateTime? LastRunAt { get; init; }
}
