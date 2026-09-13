namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 两节点间最短路径结果 — BFS 搜索调用图中的最短连接路径
/// 对齐 graphify path 命令
/// </summary>
public sealed record GraphPathResult
{
    public required string FromSymbol { get; init; }
    public required string ToSymbol { get; init; }
    public required bool PathFound { get; init; }
    public required IReadOnlyList<string> PathNodes { get; init; }
    public required IReadOnlyList<CallEdge> PathEdges { get; init; }
    public required int PathLength { get; init; }
}
