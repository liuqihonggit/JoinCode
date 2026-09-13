namespace Structura.Dag;

/// <summary>
/// DAG 边类型标签
/// </summary>
public sealed class DagEdge
{
    /// <summary>边唯一标识,默认生成 GUID(N 格式)</summary>
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    /// <summary>源节点 ID</summary>
    public required string FromId { get; init; }
    /// <summary>目标节点 ID</summary>
    public required string ToId { get; init; }
    /// <summary>边标签(语义描述),默认空字符串</summary>
    public string Label { get; init; } = string.Empty;
    /// <summary>边权重,默认 1.0</summary>
    public double Weight { get; init; } = 1.0;
}
