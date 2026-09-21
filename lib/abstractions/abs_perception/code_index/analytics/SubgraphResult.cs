namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 子图提取结果 — 以某符号为中心的 N 跳调用子图
/// </summary>
public sealed record SubgraphResult {
    /// <summary>获取中心符号。</summary>
    public required string CenterSymbol { get; init; }
    /// <summary>获取跳数。</summary>
    public required int Hops { get; init; }
    /// <summary>获取节点列表。</summary>
    public required IReadOnlyList<string> Nodes { get; init; }
    /// <summary>获取边列表。</summary>
    public required IReadOnlyList<CallEdge> Edges { get; init; }
}