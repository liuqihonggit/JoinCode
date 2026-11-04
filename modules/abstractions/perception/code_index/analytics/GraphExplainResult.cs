namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 节点解释结果 — 聚合某符号的所有关系,生成结构化描述
/// 对齐 graphify explain 命令
/// </summary>
public sealed record GraphExplainResult {
    /// <summary>获取符号名称。</summary>
    public required string SymbolName { get; init; }
    /// <summary>获取文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>获取符号种类。</summary>
    public required string Kind { get; init; }
    /// <summary>获取命名空间。</summary>
    public required string? Namespace { get; init; }
    /// <summary>获取调用方符号列表。</summary>
    public required IReadOnlyList<string> Callers { get; init; }
    /// <summary>获取被调用方符号列表。</summary>
    public required IReadOnlyList<string> Callees { get; init; }
    /// <summary>获取同一社区符号列表。</summary>
    public required IReadOnlyList<string> SameCommunity { get; init; }
    /// <summary>获取同一文件符号列表。</summary>
    public required IReadOnlyList<string> SameFile { get; init; }
    /// <summary>获取入度。</summary>
    public required int InDegree { get; init; }
    /// <summary>获取出度。</summary>
    public required int OutDegree { get; init; }
}
