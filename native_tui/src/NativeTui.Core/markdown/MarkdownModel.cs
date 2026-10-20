namespace NativeTui.Core.Markdown;

/// <summary>
/// Markdown 行内语义样式（flags）— 投影期产物，主题/宽度无关：
/// Bold/Italic/Underline/Dim/Strikethrough 为修饰开关，Code/Link 为语义标签
/// （渲染期取色：Code → theme.Permission，对齐基准 codespan color('permission')）。
/// 嵌套强调（***x***）= flags 组合；块级样式（标题/引用）由布局层并入段 flags。
/// </summary>
[Flags]
public enum MarkdownStyle : ushort
{
    /// <summary>无样式。</summary>
    None = 0,

    /// <summary>粗体（基准 strong）。</summary>
    Bold = 1 << 0,

    /// <summary>斜体（基准 em / 引用内容）。</summary>
    Italic = 1 << 1,

    /// <summary>下划线（基准 h1）。</summary>
    Underline = 1 << 2,

    /// <summary>暗淡（引用条等）。</summary>
    Dim = 1 << 3,

    /// <summary>删除线。</summary>
    Strikethrough = 1 << 4,

    /// <summary>行内代码（渲染期 permission 色）。</summary>
    Code = 1 << 5,

    /// <summary>链接（首版无 OSC 8，纯文本降级；标签保留给后续超链接支持）。</summary>
    Link = 1 << 6,
}

/// <summary>一行内一段同样式文本 — Markdown 行模型的最小单元。</summary>
public readonly record struct MarkdownSegment(string Text, MarkdownStyle Style);

/// <summary>Markdown 块模型（抽象基类）— 投影器产出，布局器消费。</summary>
public abstract record MarkdownBlock;

/// <summary>段落 — 行内段序列，布局期按宽度软换行。</summary>
public sealed record MarkdownParagraph(IReadOnlyList<MarkdownSegment> Segments) : MarkdownBlock;

/// <summary>标题（Level 1-6）— 渲染样式按级别：h1 加粗+斜体+下划线，h2+ 仅加粗（基准 formatToken）。</summary>
public sealed record MarkdownHeading(int Level, IReadOnlyList<MarkdownSegment> Segments) : MarkdownBlock;

/// <summary>围栏/缩进代码块 — Language 原样保留（M7-5 高亮消费），Text 逐行原文不换行。</summary>
public sealed record MarkdownCode(string Language, string Text) : MarkdownBlock;

/// <summary>引用块 — 子块递归，布局期每行加 ▎ dim 前缀 + 内容叠斜体（基准 blockquote）。</summary>
public sealed record MarkdownQuote(IReadOnlyList<MarkdownBlock> Children) : MarkdownBlock;

/// <summary>列表项 — 项内容为子块序列（通常一个段落，嵌套列表为其后子块）。</summary>
public sealed record MarkdownListItem(IReadOnlyList<MarkdownBlock> Children);

/// <summary>列表 — Ordered + Start（有序起始编号）；无序 "- " 前缀，有序 "N. " 前缀，2 空格/层缩进。</summary>
public sealed record MarkdownList(bool Ordered, int Start, IReadOnlyList<MarkdownListItem> Items) : MarkdownBlock;

/// <summary>分隔线 — 渲染为 "---" 三字符（基准 hr，非全宽线）。</summary>
public sealed record MarkdownRule() : MarkdownBlock;

/// <summary>表格 — 单元格为段序列（保留行内样式）；列宽 = max(显示宽, 3)，"|" 竖线 + "---" 分隔行（基准 formatToken table）。</summary>
public sealed record MarkdownTable(
    IReadOnlyList<IReadOnlyList<MarkdownSegment>> Headers,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownSegment>>> Rows) : MarkdownBlock;

/// <summary>Markdown 文档根 — 顶层块序列；顶层块间空 1 行（对齐基准 Box gap 视觉主路径）。</summary>
public sealed record MarkdownDocument(IReadOnlyList<MarkdownBlock> Blocks);
