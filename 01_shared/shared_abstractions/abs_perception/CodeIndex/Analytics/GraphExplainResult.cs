namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 节点解释结果 — 聚合某符号的所有关系,生成结构化描述
/// 对齐 graphify explain 命令
/// </summary>
public sealed record GraphExplainResult
{
    public required string SymbolName { get; init; }
    public required string FilePath { get; init; }
    public required string Kind { get; init; }
    public required string? Namespace { get; init; }
    public required IReadOnlyList<string> Callers { get; init; }
    public required IReadOnlyList<string> Callees { get; init; }
    public required IReadOnlyList<string> SameCommunity { get; init; }
    public required IReadOnlyList<string> SameFile { get; init; }
    public required int InDegree { get; init; }
    public required int OutDegree { get; init; }
}
