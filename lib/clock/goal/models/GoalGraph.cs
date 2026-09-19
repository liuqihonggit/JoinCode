namespace Core.Goal;


/// <summary>
/// Goal Graph — 基于 Dag&lt;GoalNodePayload&gt; 的图定义
/// </summary>
public sealed class GoalGraph {
    private readonly HashSet<string> _endNodeIds;

    /// <summary>图名称</summary>
    public required string Name { get; init; }
    /// <summary>底� DAG 结构</summary>
    public required Dag<GoalNodePayload> Dag { get; init; }
    /// <summary>起始节点 ID</summary>
    public required string StartNodeId { get; init; }
    /// <summary>终止节点 ID 集合（不可变快照）</summary>
    public required FrozenSet<string> EndNodeIds { get; init; }
    /// <summary>每节点最大重试次数，缺省 3</summary>
    public int MaxRetriesPerNode { get; init; } = 3;

    /// <summary>循环硬上限迭代数，缺省 16</summary>
    public int HardMaxLoopIterations { get; init; } = 16;

    /// <summary>
    /// 构造 GoalGraph — 初始化可变终止节点集合
    /// </summary>
    public GoalGraph() {
        _endNodeIds = [];
    }

    /// <summary>
    /// 查找节点 — 按 ID 从 DAG 中获取节点负载
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    /// <returns>节点负载，不存在返回 null</returns>
    public GoalNodePayload? FindNode(string nodeId)
        => Dag.Nodes.TryGetValue(nodeId, out var node) ? node.Payload : null;

    /// <summary>
    /// 判断是否为终止节点 — 优先检查运行时追加的终止节点
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    /// <returns>是终止节点返回 true，否则 false</returns>
    public bool IsEndNode(string nodeId)
        => _endNodeIds.Count > 0 ? _endNodeIds.Contains(nodeId) : EndNodeIds.Contains(nodeId);

    /// <summary>
    /// 追加终止节点 — 运行时动态添加终止节点到可变集合
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    public void AddEndNode(string nodeId) {
        _endNodeIds.Add(nodeId);
    }

    /// <summary>
    /// 获取生效的终止节点 ID 集合 — 优先返回运行时追加的集合
    /// </summary>
    /// <returns>终止节点 ID 只读集合</returns>
    public IReadOnlySet<string> GetEffectiveEndNodeIds()
        => _endNodeIds.Count > 0 ? _endNodeIds : EndNodeIds;
}