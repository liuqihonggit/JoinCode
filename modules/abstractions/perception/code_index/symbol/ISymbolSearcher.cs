namespace JoinCode.Abstractions.CodeIndex;

public interface ISymbolSearcher {
    /// <summary>异步搜索符号。</summary>
    /// <param name="query">查询文本。</param>
    /// <param name="ct">取消令牌。</param>
    Task<SearchResult<SymbolInfo>> SearchAsync(string query, CancellationToken ct);
    /// <summary>异步按符号种类搜索。</summary>
    /// <param name="kind">符号种类。</param>
    /// <param name="ct">取消令牌。</param>
    Task<SearchResult<SymbolInfo>> SearchByKindAsync(SymbolKind kind, CancellationToken ct);
    /// <summary>异步查找符号定义。</summary>
    /// <param name="symbolName">符号名称。</param>
    /// <param name="ct">取消令牌。</param>
    Task<SymbolInfo?> FindDefinitionAsync(string symbolName, CancellationToken ct);
    /// <summary>异步查找符号引用。</summary>
    /// <param name="symbolName">符号名称。</param>
    /// <param name="ct">取消令牌。</param>
    Task<IReadOnlyList<SymbolInfo>> FindReferencesAsync(string symbolName, CancellationToken ct);

    /// <summary>
    /// 按正则模式模糊搜索符号(rg式检索) — 在内存符号索引中匹配 Name + FQN
    /// </summary>
    /// <param name="pattern">正则表达式(如 "Process.*", "Service\\d+")</param>
    /// <param name="maxResults">最大返回数(避免结果过大)</param>
    /// <param name="ct">取消令牌</param>
    Task<SearchResult<SymbolInfo>> SearchByPatternAsync(string pattern, int maxResults, CancellationToken ct);
}