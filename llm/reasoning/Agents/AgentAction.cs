namespace JoinCode.Reasoning.Agents;

/// <summary>
/// Agent执行动作
/// </summary>
public sealed class AgentAction
{
    /// <summary>
    /// 执行该动作的 Agent 角色
    /// </summary>
    public required AgentRole AgentRole { get; init; }

    /// <summary>
    /// 动作类型标识
    /// </summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// 支持性证据列表
    /// </summary>
    public List<EvidenceRecord> Evidence { get; init; } = [];

    /// <summary>
    /// 反对性证据列表
    /// </summary>
    public List<EvidenceRecord> CounterEvidence { get; init; } = [];

    /// <summary>
    /// 受影响的论断标识列表
    /// </summary>
    public List<string> AffectedClaimIds { get; init; } = [];

    /// <summary>
    /// 疑点列表
    /// </summary>
    public List<string> Doubts { get; init; } = [];

    /// <summary>
    /// 裁决列表
    /// </summary>
    public List<Verdict> Verdicts { get; init; } = [];

    /// <summary>
    /// 消耗的 Token 数量
    /// </summary>
    public int TokensUsed { get; set; }
}
