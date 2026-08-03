namespace Core.Goal;

using JoinCode.Abstractions.Models.Goal;
using Structura.Dag;

/// <summary>
/// Goal Graph — 基于 Dag&lt;GoalNodePayload&gt; 的图定义
/// </summary>
public sealed class GoalGraph
{
    public required string Name { get; init; }
    public required Dag<GoalNodePayload> Dag { get; init; }
    public required string StartNodeId { get; init; }
    public required FrozenSet<string> EndNodeIds { get; init; }
    public int MaxRetriesPerNode { get; init; } = 3;

    /// <summary>
    /// 循环迭代硬上限（纵深防御，即使所有终止条件失效也强制终止）
    /// </summary>
    public int HardMaxLoopIterations { get; init; } = 16;

    public GoalNodePayload? FindNode(string nodeId)
        => Dag.Nodes.TryGetValue(nodeId, out var node) ? node.Payload : null;

    public bool IsEndNode(string nodeId)
        => EndNodeIds.Contains(nodeId);
}
