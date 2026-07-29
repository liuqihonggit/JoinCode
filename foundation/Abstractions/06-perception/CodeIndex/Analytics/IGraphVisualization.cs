namespace JoinCode.Abstractions.CodeIndex;

/// <summary>
/// 图可视化接口 — 导出为 DOT/HTML 格式
/// 对齐 graphify 的 graph.html 交互式可视化 + code-review-graph 的 visualize 命令
/// </summary>
public interface IGraphVisualization
{
    /// <summary>
    /// 导出调用图为 DOT 格式(Graphviz)
    /// </summary>
    Task<string> ExportDotAsync(CancellationToken ct);

    /// <summary>
    /// 导出调用图为交互式 HTML(含 D3.js 力导向图)
    /// </summary>
    Task<string> ExportHtmlAsync(CancellationToken ct);

    /// <summary>
    /// 导出子图为 DOT 格式
    /// </summary>
    Task<string> ExportSubgraphDotAsync(string centerSymbol, int hops, CancellationToken ct);

    /// <summary>
    /// 导出代码架构为 Markdown wiki 文档
    /// 基于社区结构生成：社区概览 + 每社区核心符号 + 社区间依赖
    /// </summary>
    Task<string> ExportWikiAsync(CancellationToken ct);
}
