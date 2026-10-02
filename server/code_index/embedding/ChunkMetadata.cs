namespace JoinCode.CodeIndex.Embedding;

/// <summary>
/// 块元数据 — 搜索结果组装用，嵌入完成后只需保留定位信息。
/// </summary>
internal sealed record ChunkMetadata {
    /// <summary>块唯一标识。</summary>
    public required string ChunkId { get; init; }
    /// <summary>文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>关联符号的完全限定名。</summary>
    public required string SymbolFqn { get; init; }
    /// <summary>符号类型名称（Method/Class/Function/...），用于属性过滤。</summary>
    public string SymbolKind { get; init; } = string.Empty;
    /// <summary>起始行号。</summary>
    public required int StartLine { get; init; }
    /// <summary>结束行号。</summary>
    public required int EndLine { get; init; }
    /// <summary>父文档块 ID — null 表示无父文档（类级块或未启用父文档检索）。</summary>
    public string? ParentChunkId { get; init; }
    /// <summary>块原文 — 嵌入后保留供 IncludeSourceText=true 时返回。</summary>
    public string? SourceText { get; init; }
    /// <summary>SourceText 在索引文件中的偏移量（-1=无SourceText或已加载到SourceText字段）。</summary>
    public long SourceTextOffset { get; init; } = -1;
    /// <summary>SourceText 字节长度。</summary>
    public int SourceTextLen { get; init; }
    /// <summary>块覆盖的 AST 符号 FQN 列表 — 知识图谱关联用。</summary>
    public IReadOnlyList<string> ContainedSymbolFqns { get; init; } = [];
}
