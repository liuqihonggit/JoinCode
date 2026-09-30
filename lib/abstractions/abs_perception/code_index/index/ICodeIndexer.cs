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
    IBinaryPersistence Persistence { get; }
    /// <summary>获取图可视化器。</summary>
    IGraphVisualization Visualization { get; }

    /// <summary>
    /// 自动加载已持久化的索引(若存在且尚未加载)。
    /// 统一加载符号索引(code-index.bin) + 向量索引(vector_index.bin) + 父文档(parent_docs.bin)。
    /// persistDir 为 null 时从当前工作目录向上发现 .git 根,加载 &lt;root&gt;/.jcc/code-index/。
    /// persistDir 不为 null 时从指定目录加载（支持外部持久化路径重定向）。
    /// 用 Interlocked 保证只执行一次,后续调用立即返回。跨进程索引复用的入口。
    /// 不自动重建索引 — 索引为空时请用 <see cref="RebuildIndexAsync"/> 显式构建。
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <param name="persistDir">持久化目录路径(null 时自动发现 git 工作区根)。</param>
    Task EnsureIndexLoadedAsync(CancellationToken ct, string? persistDir = null);

    /// <summary>
    /// 显式重建索引并持久化到磁盘 — 供斜杠命令 /index 调用。
    /// 查找 git 工作区根,构建全部符号/调用/依赖索引,持久化到 &lt;root&gt;/.jcc/code-index/。
    /// </summary>
    Task RebuildIndexAsync(CancellationToken ct);

    /// <summary>
    /// 综合检索: rg式模糊匹配符号 → 获取全部函数引用 + 调用方/被调用方,受 token 预算限制
    /// 用于: 用户只记得模糊名称时,先模糊检索候选,再用 AST 精确捞出全部引用
    /// </summary>
    /// <param name="pattern">正则表达式(模糊匹配符号 Name/FQN)</param>
    /// <param name="maxTokenBudget">返回结果的 token 预算上限(约 4 字符/token),超限则截断</param>
    /// <param name="includeAst">是否包含 AST 扩展搜索(引用+调用方/被调用方),默认 true; 设 false 仅返回符号匹配结果,节省 token</param>
    /// <param name="ct">取消令牌</param>
    Task<ComprehensiveSearchResult> SearchComprehensiveAsync(string pattern, int maxTokenBudget, CancellationToken ct, bool includeAst = true);

    /// <summary>
    /// 语义搜索 — 通过向量嵌入查找相似代码块（按语义相似度召回）。
    /// <para>未启用向量索引时返回空列表。</para>
    /// <para>options.IncludeSourceText=true 时结果携带块原文（函数源码）。</para>
    /// <para>options.IncludeParentDocument=true 时结果携带父文档原文（类/文件完整源码）。</para>
    /// </summary>
    /// <param name="query">查询文本（自然语言或代码片段）。</param>
    /// <param name="topK">返回结果数上限。</param>
    /// <param name="ct">取消令牌。</param>
    /// <param name="options">搜索选项 — AI 动态控制召回策略（null 用默认：无块原文+有父文档）。</param>
    /// <returns>匹配的代码块列表，按相似度降序排列。</returns>
    Task<IReadOnlyList<ChunkSearchResult>> SearchSemanticAsync(
        string query, int topK, CancellationToken ct, SearchOptions? options = null);

}
