namespace JoinCode.Abstractions.CodeIndex;

public interface ISymbolSearcher
{
    Task<SearchResult<SymbolInfo>> SearchAsync(string query, CancellationToken ct);
    Task<SearchResult<SymbolInfo>> SearchByKindAsync(SymbolKind kind, CancellationToken ct);
    Task<SymbolInfo?> FindDefinitionAsync(string symbolName, CancellationToken ct);
    Task<IReadOnlyList<SymbolInfo>> FindReferencesAsync(string symbolName, CancellationToken ct);

    /// <summary>
    /// 按正则模式模糊搜索符号(rg式检索) — 在内存符号索引中匹配 Name + FQN
    /// </summary>
    /// <param name="pattern">正则表达式(如 "Process.*", "Service\\d+")</param>
    /// <param name="maxResults">最大返回数(避免结果过大)</param>
    /// <param name="ct">取消令牌</param>
    Task<SearchResult<SymbolInfo>> SearchByPatternAsync(string pattern, int maxResults, CancellationToken ct);
}
