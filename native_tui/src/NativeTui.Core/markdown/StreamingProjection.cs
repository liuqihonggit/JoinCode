namespace NativeTui.Core.Markdown;

/// <summary>
/// 流式 Markdown 增量投影器（方案 B — 块级增量，对齐基准 StreamingMarkdown 的块边界切分机制）：
/// 宿主卫星注入 Core（ADR 0140 — Markdig 依赖隔离在卫星层，对齐 ICodeHighlighter 注入先例）；
/// 输入累积全文，按最后一个顶层块边界切分 — 边界前为稳定前缀（完整块投影 memoize，
/// 切分点推进才追加投影，永不重投影），最后块为增长尾（每 delta 重新解析）。
/// null 注入 = 流式项降级纯文本（M8-1 行为保留）。
/// </summary>
public interface IStreamingMarkdownProjector
{
    /// <summary>投影累积文本 — 返回稳定前缀块列表 + 增长尾文本 + 增长尾块模型；
    /// 实现方 memoize：相同累积文本长度直接返回缓存投影（帧内重复调用零解析成本）。</summary>
    StreamingMarkdownProjection Project(string accumulated);
}

/// <summary>
/// 流式投影结果 — StableBlocks：稳定前缀完整块（渲染走 MarkdownLayout 布局，含行内样式，
/// 块间空行由布局层补 — 与落定后 MarkdownMessageItem 全量渲染视觉一致，替换无跳变）；
/// Tail：最后块边界之后的增长中文本（诊断/长度判断用）；
/// TailBlocks：增长尾的块模型（TASK069-S2 — 对齐基准 `StreamingMarkdown` 把 unstableSuffix
/// 同样交给 `&lt;Markdown&gt;` 完整渲染；未闭合围栏在解析期即为 FencedCodeBlock，按代码样式呈现，
/// 不再用 dim 提示）。null = 投影器未提供尾块（纯文本行降级 — 无 Markdown 语法快路径）。
/// </summary>
public sealed record StreamingMarkdownProjection(
    IReadOnlyList<MarkdownBlock> StableBlocks,
    string Tail,
    IReadOnlyList<MarkdownBlock>? TailBlocks = null);
