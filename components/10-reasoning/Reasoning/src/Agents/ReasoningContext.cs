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

    /// <summary>
    /// 视锥调度器 — 管理多角色的有限视锥
    /// </summary>
    public ConeOrchestrator? ConeOrchestrator { get; init; }

    /// <summary>
    /// 获取当前角色的视锥上下文（LLM友好输入）
    /// </summary>
    public string GetConeContextForRole(AgentRole role)
    {
        var cone = ConeOrchestrator?.GetRole(role);
        return cone?.GetConeContext() ?? string.Empty;
    }
}
