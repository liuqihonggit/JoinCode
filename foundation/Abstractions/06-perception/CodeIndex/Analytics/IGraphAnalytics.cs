namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 图分析接口 — 社区检测、枢纽分析、死代码检测、环检测、拓扑排序、子图提取、变更影响
/// 复用 Structura DAG 算法: 环检测(WouldCreateCycle/FindAllCycles)、拓扑排序(TopologicalSort)、增量子图(GetAffectedSubgraph)
/// </summary>
public interface IGraphAnalytics
{
    /// <summary>
    /// 社区检测 — 基于标签传播算法将符号聚类为社区(模块/子系统)
    /// 对齐 graphify 的 community detection
    /// </summary>
    Task<IReadOnlyList<CommunityInfo>> DetectCommunitiesAsync(CancellationToken ct);

    /// <summary>
    /// 枢纽节点分析 — 按入度+出度排序,找出被最多符号依赖/调用的枢纽
    /// 对齐 graphify 的 hub analysis
    /// </summary>
    Task<IReadOnlyList<HubNodeInfo>> GetHubNodesAsync(int topN, CancellationToken ct);

    /// <summary>
    /// 死代码检测 — 找出从未被调用的非入口方法(无调用方的非 public 方法)
    /// 对齐 code-review-graph 的 dead code detection
    /// </summary>
    Task<IReadOnlyList<DeadCodeEntry>> DetectDeadCodeAsync(CancellationToken ct);

    /// <summary>
    /// 提取子图 — 以指定符号为中心,提取 N 跳范围内的调用图子图
    /// 对齐 code-review-graph 的 subgraph extraction
    /// </summary>
    Task<SubgraphResult> ExtractSubgraphAsync(string centerSymbol, int hops, CancellationToken ct);

    /// <summary>
    /// 变更影响分析 — 分析指定文件集的变更对全局调用图的影响范围
    /// 对齐 code-review-graph 的 detect-changes / blast radius
    /// 复用 Structura DAG 的 GetAffectedSubgraph 增量算法
    /// </summary>
    Task<ChangeImpactResult> AnalyzeChangeImpactAsync(IReadOnlyList<string> changedFiles, CancellationToken ct);

    /// <summary>
    /// 环检测 — 检测调用图/依赖图中的循环依赖
    /// 复用 Structura DAG 的 FindAllCycles 算法
    /// </summary>
    Task<CycleDetectionResult> DetectCyclesAsync(CancellationToken ct);

    /// <summary>
    /// 拓扑排序 — 按依赖关系排序符号(编译顺序/初始化顺序)
    /// 复用 Structura DAG 的 TopologicalSortByLevels 算法
    /// </summary>
    Task<IReadOnlyList<IReadOnlyList<string>>> TopologicalSortByLevelsAsync(CancellationToken ct);
}
