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

    public GoalNodePayload? FindNode(string nodeId)
        => Dag.Nodes.TryGetValue(nodeId, out var node) ? node.Payload : null;

    public bool IsEndNode(string nodeId)
        => EndNodeIds.Contains(nodeId);
}
