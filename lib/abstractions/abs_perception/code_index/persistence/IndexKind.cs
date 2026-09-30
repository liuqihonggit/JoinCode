namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 索引类型标识 — [Flags] 支持位运算组合（A | B | C）。
/// <para>消费者用 IndexKind.Symbol | IndexKind.Vector 指定加载符号+向量，IndexKind.All 加载全部。</para>
/// </summary>
[Flags]
public enum IndexKind {
    /// <summary>无。</summary>
    None = 0,
    /// <summary>符号索引（code-index.bin）— AST 符号、调用图、依赖图。</summary>
    Symbol = 1,
    /// <summary>向量索引（vector_index.bin）— 语义嵌入、ANN 搜索。</summary>
    Vector = 2,
    /// <summary>父文档索引（parent_docs.bin）— 类/文件完整源码召回。</summary>
    Parent = 4,
    /// <summary>全部索引。</summary>
    All = Symbol | Vector | Parent,
}
