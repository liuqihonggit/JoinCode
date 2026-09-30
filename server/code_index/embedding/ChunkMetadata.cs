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
    /// <summary>起始行号。</summary>
    public required int StartLine { get; init; }
    /// <summary>结束行号。</summary>
    public required int EndLine { get; init; }
}
