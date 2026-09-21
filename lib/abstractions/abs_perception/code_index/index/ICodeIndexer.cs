namespace JoinCode.Abstractions.CodeIndex;

public interface ICodeIndexer {
    /// <summary>构建代码索引。</summary>
    /// <param name="options">索引选项。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="progress">进度回调。</param>
    Task<BuildIndexResult> BuildIndexAsync(CodeIndexOptions options, CancellationToken ct, IProgress<IndexProgress>? progress = null);
    /// <summary>更新指定文件的索引。</summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    Task UpdateFileAsync(string filePath, CancellationToken ct);
    /// <summary>移除指定文件的索引。</summary>
    /// <param name="filePath">文件路径。</param>
    /// <param name="ct">取消令牌。</param>
    Task RemoveFileAsync(string filePath, CancellationToken ct);
    /// <summary>获取索引统计信息。</summary>
    /// <param name="ct">取消令牌。</param>
    Task<IndexStats> GetStatsAsync(CancellationToken ct);
    /// <summary>获取符号搜索器。</summary>
    ISymbolSearcher Searcher { get; }
    /// <summary>获取调用图。</summary>
    ICallGraph CallGraph { get; }
    /// <summary>获取依赖图。</summary>
    IDependencyGraph DependencyGraph { get; }
    /// <summary>获取项目依赖图。</summary>
    IProjectDependencyGraph ProjectDependencyGraph { get; }
    /// <summary>获取图分析器。</summary>
    IGraphAnalytics Analytics { get; }
    /// <summary>获取图持久化器。</summary>
    IGraphPersistence Persistence { get; }
    /// <summary>获取图可视化器。</summary>
    IGraphVisualization Visualization { get; }

    /// <summary>
    /// 自动加载已持久化的索引(若存在且尚未加载)。
    /// 从当前工作目录向上发现 .git 根,加载 &lt;root&gt;/.jcc/code-index/code-index.json。
    /// 用 Interlocked 保证只执行一次,后续调用立即返回。跨进程索引复用的入口。
    /// </summary>
    Task EnsureIndexLoadedAsync(CancellationToken ct);

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
