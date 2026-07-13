namespace JoinCode.Reasoning.Agents;

/// <summary>
/// Agent执行动作
/// </summary>
public sealed class AgentAction
{
    public required AgentRole AgentRole { get; init; }
    public string ActionType { get; set; } = string.Empty;
    public List<EvidenceRecord> Evidence { get; init; } = [];
    public List<EvidenceRecord> CounterEvidence { get; init; } = [];
    public List<string> AffectedClaimIds { get; init; } = [];
    public List<string> Doubts { get; init; } = [];
    public List<Verdict> Verdicts { get; init; } = [];
    public int TokensUsed { get; set; }
}
