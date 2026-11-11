namespace Core.Agents.Coordinator.Liveness;

/// <summary>
/// 链路卡死检测结果 — 沿 ParentSessionId 链聚合判定（ADR 0106 L2 检测层）
/// </summary>
/// <param name="Chain">链路上所有节点 ID（从叶子到根）</param>
/// <param name="TotalNodes">链路总节点数</param>
/// <param name="ConfirmedNodes">链路上已确认卡死的节点数</param>
/// <param name="IsChainStalled">是否整链卡死（所有节点都 Confirmed 且达到阈值）</param>
public sealed record ChainStallResult(
    IReadOnlyList<string> Chain,
    int TotalNodes,
    int ConfirmedNodes,
    bool IsChainStalled) {
    /// <summary>未检测到链路卡死的默认结果</summary>
    public static readonly ChainStallResult NotStalled = new([], 0, 0, false);
}

/// <summary>
/// 子代理链路卡死检测器 — 沿父子关系链聚合判定（ADR 0106 L2 检测层）
/// <para>
/// 单点卡死由 <see cref="SubAgentIdleDetector"/> 处理；整链卡死说明问题在更上层或资源层，需 L4 恢复介入。
/// 链路构建：从叶子节点沿 parentMap 向上回溯，硬上限 100 层防递归爆炸。
/// </para>
/// </summary>
public sealed class SubAgentChainStallDetector {
    private readonly int _chainStallThreshold;

    /// <summary>
    /// 初始化链路卡死检测器
    /// </summary>
    /// <param name="chainStallThreshold">链路卡死节点数阈值，默认 3</param>
    public SubAgentChainStallDetector(int chainStallThreshold = 3) {
        ArgumentOutOfRangeException.ThrowIfLessThan(chainStallThreshold, 1);
        _chainStallThreshold = chainStallThreshold;
    }

    /// <summary>
    /// 检查从指定 agent 出发的链路是否全卡死
    /// </summary>
    /// <param name="rootAgentId">起始 agent ID（通常是叶子节点）</param>
    /// <param name="parentMap">父子关系映射：agentId → parentAgentId</param>
    /// <param name="confirmedAgentIds">已确认卡死的 agent ID 集合</param>
    /// <returns>链路卡死判定结果</returns>
    public ChainStallResult CheckChain(
        string rootAgentId,
        IReadOnlyDictionary<string, string> parentMap,
        IReadOnlySet<string> confirmedAgentIds) {
        ArgumentNullException.ThrowIfNull(rootAgentId);
        ArgumentNullException.ThrowIfNull(parentMap);
        ArgumentNullException.ThrowIfNull(confirmedAgentIds);

        var chain = BuildChain(rootAgentId, parentMap);
        var confirmedCount = 0;
        foreach (var id in chain) {
            if (confirmedAgentIds.Contains(id))
                confirmedCount++;
        }

        var isChainStalled = confirmedCount >= _chainStallThreshold && confirmedCount == chain.Count;
        return new ChainStallResult(chain, chain.Count, confirmedCount, isChainStalled);
    }

    /// <summary>
    /// 批量检查所有已确认 agent 的链路，返回所有整链卡死的链
    /// </summary>
    /// <param name="confirmedAgentIds">已确认卡死的 agent ID 集合</param>
    /// <param name="parentMap">父子关系映射：agentId → parentAgentId</param>
    /// <returns>所有整链卡死的链路结果</returns>
    public IReadOnlyList<ChainStallResult> CheckAllChains(
        IReadOnlySet<string> confirmedAgentIds,
        IReadOnlyDictionary<string, string> parentMap) {
        ArgumentNullException.ThrowIfNull(confirmedAgentIds);
        ArgumentNullException.ThrowIfNull(parentMap);

        var results = new List<ChainStallResult>();
        foreach (var agentId in confirmedAgentIds) {
            var result = CheckChain(agentId, parentMap, confirmedAgentIds);
            if (result.IsChainStalled)
                results.Add(result);
        }
        return results;
    }

    /// <summary>
    /// 沿 parentMap 向上回溯构建链路 — 硬上限 100 层防递归爆炸
    /// </summary>
    private static List<string> BuildChain(string agentId, IReadOnlyDictionary<string, string> parentMap) {
        var chain = new List<string> { agentId };
        var current = agentId;
        for (var i = 0; i < 100; i++) {
            if (!parentMap.TryGetValue(current, out var parent)) break;
            if (parent == current) break; // 自环保护
            chain.Add(parent);
            current = parent;
        }
        return chain;
    }
}