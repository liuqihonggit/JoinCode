namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 语义搜索结果 — 匹配的代码块及其相似度分数。
/// </summary>
public sealed record ChunkSearchResult {
    /// <summary>块唯一标识。</summary>
    public required string ChunkId { get; init; }
    /// <summary>文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>关联符号的完全限定名。</summary>
    public required string SymbolFqn { get; init; }
    /// <summary>起始行号。</summary>
    public required int StartLine { get; init; }
    /// <summary>结束行号。</summary>
    public required int EndLine { get; init; }
    /// <summary>余弦相似度分数（0~1，越大越相似）。</summary>
    public required float Score { get; init; }
}
