namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 代码块 — 向量嵌入的输入单元，与 SymbolInfo 分离。
/// <para>SymbolInfo 只含元数据（符号图用，内存友好）。</para>
/// <para>ChunkInfo 额外持有源码片段（嵌入用，嵌入完成后可置 null 释放内存）。</para>
/// </summary>
public sealed record ChunkInfo {
    /// <summary>块唯一标识 = hash(filePath + fqn + contentHash)。</summary>
    public required string ChunkId { get; init; }
    /// <summary>关联符号的完全限定名。</summary>
    public required string SymbolFqn { get; init; }
    /// <summary>符号类型（Method/Class/Function/...）。</summary>
    public required SymbolKind Kind { get; init; }
    /// <summary>文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>起始行号。</summary>
    public required int StartLine { get; init; }
    /// <summary>结束行号。</summary>
    public required int EndLine { get; init; }
    /// <summary>语言标识（c-sharp/python/rust/go/...）。</summary>
    public required string LanguageId { get; init; }
    /// <summary>块内容哈希 — 增量缓存键，哈希不变则跳过嵌入。</summary>
    public required string ContentHash { get; init; }
    /// <summary>块源码文本 — 嵌入输入，嵌入完成后可置 null 释放内存。</summary>
    public string? SourceText { get; init; }
    /// <summary>
    /// 父文档块 ID — 父文档检索用，指向所属类/文件的 ChunkId。
    /// <para>null 表示该块本身是父文档（类/文件级）或未启用父文档检索。</para>
    /// </summary>
    public string? ParentChunkId { get; init; }
}
