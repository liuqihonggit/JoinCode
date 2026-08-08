namespace JoinCode.Gui.Markdown;

/// <summary>
/// Markdown 渲染模型 — 纯 DTO（无 Avalonia 依赖），由 <see cref="MarkdownParser"/>
/// 从 Markdig AST 转换而来，再由 View 层渲染为控件树。
/// 分层目的：模型与渲染逻辑可用 xUnit 直接测试，无需 Headless 渲染环境。
/// </summary>
public abstract record MarkdownBlock;

/// <summary>标题块</summary>
public sealed record MarkdownHeading(int Level, IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>段落块</summary>
public sealed record MarkdownParagraph(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>围栏代码块（含语言标识与原文）</summary>
public sealed record MarkdownCodeBlock(string? Language, string Code) : MarkdownBlock;

/// <summary>列表块（有序/无序，逐项内联）</summary>
public sealed record MarkdownList(bool Ordered, IReadOnlyList<IReadOnlyList<MarkdownInline>> Items) : MarkdownBlock;

/// <summary>引用块</summary>
public sealed record MarkdownQuote(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>表格块（首行为表头，后续为数据行）</summary>
public sealed record MarkdownTable(
    IReadOnlyList<string> Header,
    IReadOnlyList<IReadOnlyList<string>> Rows) : MarkdownBlock;

/// <summary>分隔线</summary>
public sealed record MarkdownRule : MarkdownBlock;

/// <summary>Markdown 内联元素基类</summary>
public abstract record MarkdownInline;

/// <summary>纯文本内联</summary>
public sealed record MarkdownText(string Text) : MarkdownInline;

/// <summary>粗体内联</summary>
public sealed record MarkdownBold(IReadOnlyList<MarkdownInline> Children) : MarkdownInline;

/// <summary>斜体内联</summary>
public sealed record MarkdownItalic(IReadOnlyList<MarkdownInline> Children) : MarkdownInline;

/// <summary>行内代码（等宽展示）</summary>
public sealed record MarkdownCode(string Code) : MarkdownInline;

/// <summary>链接内联</summary>
public sealed record MarkdownLink(string Url, IReadOnlyList<MarkdownInline> Children) : MarkdownInline;

/// <summary>删除线内联</summary>
public sealed record MarkdownStrikethrough(IReadOnlyList<MarkdownInline> Children) : MarkdownInline;
