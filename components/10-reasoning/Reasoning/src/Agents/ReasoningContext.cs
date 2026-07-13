namespace JoinCode.Reasoning.Agents;

/// <summary>
/// 推理上下文 — 每轮推理传递给 Agent 的只读快照
/// 注意：Dag 引用是内部信任边界，Agent 不应通过此引用修改引擎状态
/// </summary>
public sealed class ReasoningContext
{
    public required IReadOnlyList<DataItem> AllItems { get; init; }
    public required IReadOnlyList<EvidenceRecord> AllEvidence { get; init; }
    public required Dag<ReasoningPayload> Dag { get; init; }
    public required ReasoningOptions Options { get; init; }
}
