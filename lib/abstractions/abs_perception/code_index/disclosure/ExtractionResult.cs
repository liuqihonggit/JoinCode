namespace JoinCode.Abstractions.CodeIndex;

public sealed class ExtractionResult {
    /// <summary>获取提取的符号列表。</summary>
    public required IReadOnlyList<SymbolInfo> Symbols { get; init; }
    /// <summary>获取调用边列表。</summary>
    public required IReadOnlyList<CallEdge> Calls { get; init; }
    /// <summary>获取依赖边列表。</summary>
    public required IReadOnlyList<DependencyEdge> Dependencies { get; init; }
    /// <summary>获取代码块列表 — 向量嵌入用，默认空（符号图不需要）。</summary>
    public IReadOnlyList<ChunkInfo> Chunks { get; init; } = [];
    /// <summary>
    /// 获取父文档列表 — 父文档检索用，存类/文件级完整源码原文。
    /// <para>向量库存小块（方法），召回后通过 ChunkInfo.ParentChunkId 查父文档原文。</para>
    /// <para>默认空（未启用父文档检索或符号图不需要）。</para>
    /// </summary>
    public IReadOnlyList<ParentDocument> ParentDocuments { get; init; } = [];
}