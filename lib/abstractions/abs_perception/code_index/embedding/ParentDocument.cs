namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 父文档 — 父文档检索的完整上下文单元（类或文件级源码）。
/// <para>向量库存小块（方法/属性），召回小块后取对应父文档原文喂给 LLM。</para>
/// <para>父文档本身也是一个 ChunkInfo（Kind=Class/File），有自己的 ChunkId。</para>
/// </summary>
public sealed record ParentDocument {
    /// <summary>父文档唯一标识 — 与父文档 ChunkInfo.ChunkId 一致。</summary>
    public required string ChunkId { get; init; }
    /// <summary>文件路径。</summary>
    public required string FilePath { get; init; }
    /// <summary>父文档符号完全限定名（类 FQN 或文件路径标识）。</summary>
    public required string SymbolFqn { get; init; }
    /// <summary>起始行号。</summary>
    public required int StartLine { get; init; }
    /// <summary>结束行号。</summary>
    public required int EndLine { get; init; }
    /// <summary>父文档源码原文 — 召回时喂给 LLM 的完整上下文。</summary>
    public required string SourceText { get; init; }
}
