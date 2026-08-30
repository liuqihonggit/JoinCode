namespace Core.Goal;


/// <summary>
/// Goal Graph — 基于 Dag&lt;GoalNodePayload&gt; 的图定义
/// </summary>
public sealed class GoalGraph
{
    private readonly HashSet<string> _endNodeIds;

    public required string Name { get; init; }
    public required Dag<GoalNodePayload> Dag { get; init; }
    public required string StartNodeId { get; init; }
    public required FrozenSet<string> EndNodeIds { get; init; }
    public int MaxRetriesPerNode { get; init; } = 3;

    public int HardMaxLoopIterations { get; init; } = 16;

    /// <summary>
    /// 同层节点最大并发数。0 = 无限制（按就绪节点数并发）。
    /// 1 = 退化为串行执行。对齐 ClusterExecutionOptions.MaxConcurrency。
    /// </summary>
    public int MaxConcurrency { get; init; } = 0;

    public GoalGraph()
    {
        _endNodeIds = [];
    }

    public GoalNodePayload? FindNode(string nodeId)
        => Dag.Nodes.TryGetValue(nodeId, out var node) ? node.Payload : null;

    public bool IsEndNode(string nodeId)
        => _endNodeIds.Count > 0 ? _endNodeIds.Contains(nodeId) : EndNodeIds.Contains(nodeId);

    public void AddEndNode(string nodeId)
    {
        _endNodeIds.Add(nodeId);
    }

    public IReadOnlySet<string> GetEffectiveEndNodeIds()
        => _endNodeIds.Count > 0 ? _endNodeIds : EndNodeIds;
}
