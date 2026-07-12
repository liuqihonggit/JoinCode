namespace Infrastructure.Dag;

/// <summary>
/// DAG 节点 — 泛型 payload + 状态
/// </summary>
public sealed class DagNode<T>
{
    public required string Id { get; init; }
    public required T Payload { get; init; }
    public int Version { get; set; }
    public List<string> InEdgeIds { get; init; } = [];
    public List<string> OutEdgeIds { get; init; } = [];
}
