namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 图语义查询结果 — 基于自然语言查询匹配的相关符号和子图摘要
/// 对齐 graphify query 命令
/// </summary>
public sealed record GraphQueryResult {
    /// <summary>获取查询字符串。</summary>
    public required string Query { get; init; }
    /// <summary>获取匹配项列表。</summary>
    public required IReadOnlyList<GraphQueryMatch> Matches { get; init; }
    /// <summary>获取匹配总数。</summary>
    public required int TotalMatches { get; init; }
}

/// <summary>
/// 图语义查询的单个匹配项
/// </summary>
public sealed record GraphQueryMatch {
    /// <summary>获取符号名称。</summary>
    public required string SymbolName { get; init; }
    /// <summary>获取文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>获取符号种类。</summary>
    public required string Kind { get; init; }
    /// <summary>获取相关性评分。</summary>
    public required int RelevanceScore { get; init; }
    /// <summary>获取关联符号列表。</summary>
    public required IReadOnlyList<string> RelatedSymbols { get; init; }
}
