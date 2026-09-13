namespace Structura.Dag;

/// <summary>
/// DAG 节点 — 泛型 payload + 状态
/// </summary>
public sealed class DagNode<T>
{
    /// <summary>节点唯一标识</summary>
    public required string Id { get; init; }
    /// <summary>节点携带的泛型负载</summary>
    public required T Payload { get; init; }
    /// <summary>节点版本号,变更时递增</summary>
    public int Version { get; set; }
    /// <summary>入边 ID 列表(指向本节点的边)</summary>
    public List<string> InEdgeIds { get; init; } = [];
    /// <summary>出边 ID 列表(从本节点出发的边)</summary>
    public List<string> OutEdgeIds { get; init; } = [];
}
