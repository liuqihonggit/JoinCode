namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 两节点间最短路径结果 — BFS 搜索调用图中的最短连接路径
/// 对齐 graphify path 命令
/// </summary>
public sealed record GraphPathResult {
    /// <summary>获取起始符号。</summary>
    public required string FromSymbol { get; init; }
    /// <summary>获取目标符号。</summary>
    public required string ToSymbol { get; init; }
    /// <summary>获取是否找到路径。</summary>
    public required bool PathFound { get; init; }
    /// <summary>获取路径节点列表。</summary>
    public required IReadOnlyList<string> PathNodes { get; init; }
    /// <summary>获取路径边列表。</summary>
    public required IReadOnlyList<CallEdge> PathEdges { get; init; }
    /// <summary>获取路径长度。</summary>
    public required int PathLength { get; init; }
}