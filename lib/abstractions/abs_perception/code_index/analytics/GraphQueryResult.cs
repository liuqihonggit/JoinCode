namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 图语义查询结果 — 基于自然语言查询匹配的相关符号和子图摘要
/// 对齐 graphify query 命令
/// </summary>
public sealed record GraphQueryResult
{
    public required string Query { get; init; }
    public required IReadOnlyList<GraphQueryMatch> Matches { get; init; }
    public required int TotalMatches { get; init; }
}

/// <summary>
/// 图语义查询的单个匹配项
/// </summary>
public sealed record GraphQueryMatch
{
    public required string SymbolName { get; init; }
    public required string FilePath { get; init; }
    public required string Kind { get; init; }
    public required int RelevanceScore { get; init; }
    public required IReadOnlyList<string> RelatedSymbols { get; init; }
}
