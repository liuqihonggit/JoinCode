namespace NativeTui.Core.Markdown;

/// <summary>
/// 布局行收集器 — 段行与高亮上下文双列表严格同步（普通行 Add 补 null，代码行走 AddCode）；
/// 持有高亮器（AppendCode 整块调用 — 布局期一次性算好缓存，渲染期零成本消费）。
/// </summary>
internal sealed class LayoutLines(ICodeHighlighter? highlighter)
{
    /// <summary>物理行（段序列）。</summary>
    private readonly List<IReadOnlyList<MarkdownSegment>> _lines = [];

    /// <summary>逐行高亮上下文（与 _lines 等长 — null = 非代码行）。</summary>
    private readonly List<CodeLineHighlight?> _highlights = [];

    /// <summary>高亮器（null = 高亮禁用 — 代码块降级纯文本）。</summary>
    public ICodeHighlighter? Highlighter => highlighter;

    /// <summary>追加普通行（高亮上下文 = null）。</summary>
    public void Add(IReadOnlyList<MarkdownSegment> line)
    {
        _lines.Add(line);
        _highlights.Add(null);
    }

    /// <summary>追加代码行（携带该行 token 色段；null = 该行无色段，如空行）。</summary>
    public void AddCode(IReadOnlyList<MarkdownSegment> line, CodeLineHighlight? highlight)
    {
        _lines.Add(line);
        _highlights.Add(highlight);
    }

    /// <summary>产出布局结果（行 + 高亮等长对齐）。</summary>
    public MarkdownLayoutResult ToResult() => new(_lines, _highlights);
}
