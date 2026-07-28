namespace JoinCode.Abstractions.CodeIndex;

public interface ICodeIndexer
{
    Task<BuildIndexResult> BuildIndexAsync(CodeIndexOptions options, CancellationToken ct, IProgress<IndexProgress>? progress = null);
    Task UpdateFileAsync(string filePath, CancellationToken ct);
    Task RemoveFileAsync(string filePath, CancellationToken ct);
    Task<IndexStats> GetStatsAsync(CancellationToken ct);
    ISymbolSearcher Searcher { get; }
    ICallGraph CallGraph { get; }
    IDependencyGraph DependencyGraph { get; }
    IProjectDependencyGraph ProjectDependencyGraph { get; }

    /// <summary>
    /// 综合检索: rg式模糊匹配符号 → 获取全部函数引用 + 调用方/被调用方,受 token 预算限制
    /// 用于: 用户只记得模糊名称时,先模糊检索候选,再用 AST 精确捞出全部引用
    /// </summary>
    /// <param name="pattern">正则表达式(模糊匹配符号 Name/FQN)</param>
    /// <param name="maxTokenBudget">返回结果的 token 预算上限(约 4 字符/token),超限则截断</param>
    /// <param name="includeAst">是否包含 AST 扩展搜索(引用+调用方/被调用方),默认 true; 设 false 仅返回符号匹配结果,节省 token</param>
    /// <param name="ct">取消令牌</param>
    Task<ComprehensiveSearchResult> SearchComprehensiveAsync(string pattern, int maxTokenBudget, CancellationToken ct, bool includeAst = true);
}
