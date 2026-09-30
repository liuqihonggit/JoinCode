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
    /// <summary>父文档原文 — 父文档检索召回时填充（所属类/文件完整源码），null 表示未启用或无父文档。</summary>
    public string? ParentDocumentText { get; init; }
    /// <summary>父文档起始行号 — null 表示无父文档。</summary>
    public int? ParentStartLine { get; init; }
    /// <summary>父文档结束行号 — null 表示无父文档。</summary>
    public int? ParentEndLine { get; init; }
    /// <summary>父文档符号完全限定名 — null 表示无父文档。</summary>
    public string? ParentSymbolFqn { get; init; }
}
