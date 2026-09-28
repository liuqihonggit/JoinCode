namespace Structura.Dag;

/// <summary>
/// 不可变 DAG 节点 — InEdgeIds/OutEdgeIds 用 ImmutableHamTSet 替代可变 List，支持不可变快照
/// </summary>
public sealed record ImmutableDagNode<T> {
    /// <summary>节点唯一标识</summary>
    public required string Id { get; init; }
    /// <summary>节点携带的泛型负载</summary>
    public required T Payload { get; init; }
    /// <summary>节点版本号,变更时递增</summary>
    public int Version { get; init; }
    /// <summary>入边 ID 集合(指向本节点的边)</summary>
    public ImmutableHamTSet<string> InEdgeIds { get; init; } = ImmutableHamTSet<string>.Empty;
    /// <summary>出边 ID 集合(从本节点出发的边)</summary>
    public ImmutableHamTSet<string> OutEdgeIds { get; init; } = ImmutableHamTSet<string>.Empty;
}
