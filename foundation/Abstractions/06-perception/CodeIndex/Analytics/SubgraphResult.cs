namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 子图提取结果 — 以某符号为中心的 N 跳调用子图
/// </summary>
public sealed record SubgraphResult
{
    public required string CenterSymbol { get; init; }
    public required int Hops { get; init; }
    public required IReadOnlyList<string> Nodes { get; init; }
    public required IReadOnlyList<CallEdge> Edges { get; init; }
}
