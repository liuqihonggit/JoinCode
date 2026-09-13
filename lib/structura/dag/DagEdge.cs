namespace Structura.Dag;

/// <summary>
/// DAG 边类型标签
/// </summary>
public sealed class DagEdge
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public required string FromId { get; init; }
    public required string ToId { get; init; }
    public string Label { get; init; } = string.Empty;
    public double Weight { get; init; } = 1.0;
}
