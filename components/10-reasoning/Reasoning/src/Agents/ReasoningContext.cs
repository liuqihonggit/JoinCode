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
    /// 推理上下文压缩器 — 可选，为空时不压缩
    /// </summary>
    public IReasoningContextCompressor? ContextCompressor { get; init; }

    /// <summary>
    /// 获取当前角色的视锥上下文（LLM友好输入）
    /// </summary>
    public string GetConeContextForRole(AgentRole role)
    {
        var cone = ConeOrchestrator?.GetRole(role);
        return cone?.GetConeContext() ?? string.Empty;
    }

    /// <summary>
    /// 获取角色可见的数据项 — 基于视锥过滤，未裁决项始终可见
    /// </summary>
    public IReadOnlyList<DataItem> GetVisibleItemsForRole(AgentRole role)
    {
        if (ConeOrchestrator is null) return AllItems;

        var cone = ConeOrchestrator.GetRole(role);
        if (cone is null) return AllItems;

        var visibleSourceIds = GetVisibleSourceIds(cone);

        return AllItems
            .Where(item => visibleSourceIds.Contains(item.Id) ||
                          item.State is DataState.Assumption or DataState.PendingEvidence)
            .ToList();
    }

    /// <summary>
    /// 获取角色可见的证据 — 基于视锥过滤
    /// </summary>
    public IReadOnlyList<EvidenceRecord> GetVisibleEvidenceForRole(AgentRole role)
    {
        if (ConeOrchestrator is null) return AllEvidence;

        var cone = ConeOrchestrator.GetRole(role);
        if (cone is null) return AllEvidence;

        var visibleSourceIds = GetVisibleSourceIds(cone);

        return AllEvidence
            .Where(e => visibleSourceIds.Contains(e.Id))
            .ToList();
    }

    private static HashSet<string> GetVisibleSourceIds(RoleCone cone)
    {
        return cone.ActiveFragmentIds
            .Where(id => cone.AllFragments.ContainsKey(id))
            .Select(id => cone.AllFragments[id].SourceItemId)
            .ToHashSet();
    }
}
