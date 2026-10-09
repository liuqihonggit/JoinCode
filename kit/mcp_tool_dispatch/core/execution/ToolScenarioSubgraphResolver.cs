namespace McpToolDispatch;

/// <summary>
/// 工具情景子图 — 一个情景模式对应的 DAG 子集，只包含该情景的工具和超边
/// AI 进入情景时只看子图不见全图，蜘蛛网边（跨情景的高耦合边）在情景外不可见
/// </summary>
public sealed record ToolScenarioSubgraph {
    /// <summary>获取情景模式名称。</summary>
    public required string ScenarioName { get; init; }
    /// <summary>获取子图包含的工具集合。</summary>
    public required FrozenSet<string> Tools { get; init; }
    /// <summary>获取子图包含的超边（仅成员工具全部在 Tools 中的超边）。</summary>
    public required IReadOnlyList<ToolHyperedge> Edges { get; init; }
    /// <summary>获取被过滤的蜘蛛网边数量（跨情景的高耦合边）。</summary>
    public int FilteredEdgeCount { get; init; }
}

/// <summary>
/// 工具情景子图解析器 — 从全图和情景模式注册表解析出子图，化解 DAG 蜘蛛网
/// </summary>
public sealed class ToolScenarioSubgraphResolver {
    private readonly ToolHypergraphScorer _scorer;

    /// <summary>构造子图解析器。</summary>
    /// <param name="scorer">超图评分器（提供全图数据）</param>
    public ToolScenarioSubgraphResolver(ToolHypergraphScorer scorer) {
        _scorer = scorer;
    }

    /// <summary>
    /// 解析情景子图 — 从全图提取该情景的工具和超边，过滤蜘蛛网边
    /// </summary>
    /// <param name="scenario">情景模式信息</param>
    /// <returns>情景子图（仅含该情景的工具和超边）</returns>
    public ToolScenarioSubgraph Resolve(ScenarioInfo scenario) {
        var tools = FrozenSet.Create(StringComparer.OrdinalIgnoreCase, scenario.Tools);
        var includedEdges = new List<ToolHyperedge>();
        var seenEdges = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var filteredCount = 0;

        foreach (var toolName in scenario.Tools) {
            var edges = _scorer.GetEdges(toolName);
            foreach (var edge in edges) {
                var allMembersInScenario = edge.ToolNames.All(t => tools.Contains(t));
                if (allMembersInScenario) {
                    if (seenEdges.Add(edge.Id)) {
                        includedEdges.Add(edge);
                    }
                } else {
                    filteredCount++;
                }
            }
        }

        return new ToolScenarioSubgraph {
            ScenarioName = scenario.Name,
            Tools = tools,
            Edges = includedEdges,
            FilteredEdgeCount = filteredCount
        };
    }

    /// <summary>
    /// 获取工具在情景内的推荐链路 — 只推荐子图内的工具，蜘蛛网边不可见
    /// </summary>
    /// <param name="scenario">情景模式信息</param>
    /// <param name="toolName">当前工具名称</param>
    /// <returns>子图内的推荐链路，超出子图的工具被过滤</returns>
    public string[]? GetScopedChainRecommendations(ScenarioInfo scenario, string toolName) {
        var subgraph = Resolve(scenario);
        if (!subgraph.Tools.Contains(toolName)) return null;

        var fullChain = _scorer.GetChainRecommendations(toolName);
        if (fullChain is null or { Length: 0 }) return null;

        var scoped = fullChain.Where(t => subgraph.Tools.Contains(t)).ToArray();
        return scoped.Length > 0 ? scoped : null;
    }

    /// <summary>
    /// 判断工具是否属于指定情景
    /// </summary>
    /// <param name="scenario">情景模式信息</param>
    /// <param name="toolName">工具名称</param>
    /// <returns>属于该情景返回 true</returns>
    public bool IsToolInScenario(ScenarioInfo scenario, string toolName) {
        return scenario.Tools.Contains(toolName, StringComparer.OrdinalIgnoreCase);
    }
}
