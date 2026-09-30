namespace JoinCode.CodeIndex.Query;

/// <summary>
/// 查询类型分类。
/// </summary>
public enum QueryKind {
    /// <summary>符号型查询 — 精确查找符号名/引用/调用关系。</summary>
    Symbol,
    /// <summary>语义型查询 — 自然语言描述，需向量相似度搜索。</summary>
    Semantic,
    /// <summary>混合查询 — 同时包含符号和语义特征，双路并行。</summary>
    Hybrid
}

/// <summary>
/// 混合查询结果 — 包含语义和符号两路结果。
/// </summary>
public sealed record HybridQueryResult {
    /// <summary>语义搜索结果（向量相似度匹配）。</summary>
    public required IReadOnlyList<ChunkSearchResult> SemanticResults { get; init; }
    /// <summary>符号搜索结果（精确名/引用匹配）。</summary>
    public required IReadOnlyList<SymbolInfo> SymbolResults { get; init; }
    /// <summary>查询被分类的类型。</summary>
    public required QueryKind ClassifiedKind { get; init; }
    /// <summary>实际使用的搜索策略描述（vector/symbol/fallback/hybrid）。</summary>
    public required string UsedStrategy { get; init; }
}
