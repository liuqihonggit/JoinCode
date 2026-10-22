namespace NativeTui.Core.Markdown;

/// <summary>
/// 布局行收集器 — 段行/高亮/链接区间三列表严格同步（普通行 Add 补 null 高亮 + 自动提取链接区间，
/// 代码行走 AddCode 恒空区间表）；持有高亮器（AppendCode 整块调用 — 布局期一次性算好缓存，渲染期零成本消费）。
/// 链接区间提取（DSG043 P2）在 Add 内自动完成 — 遍历行段累计显示列宽，Url 非空段产
/// [StartCol, EndCol) 物理列区间（前缀段 Url 恒 null 零影响，列累计天然含前缀偏移）。
/// </summary>
internal sealed class LayoutLines(ICodeHighlighter? highlighter)
{
    /// <summary>物理行（段序列）。</summary>
    private readonly List<IReadOnlyList<MarkdownSegment>> _lines = [];

    /// <summary>逐行高亮上下文（与 _lines 等长 — null = 非代码行）。</summary>
    private readonly List<CodeLineHighlight?> _highlights = [];

    /// <summary>逐行链接区间表（与 _lines 等长 — 空表 = 该行无链接）。</summary>
    private readonly List<IReadOnlyList<LinkSpan>> _links = [];

    /// <summary>高亮器（null = 高亮禁用 — 代码块降级纯文本）。</summary>
    public ICodeHighlighter? Highlighter => highlighter;

    /// <summary>追加普通行（高亮上下文 = null；链接区间自动提取）。</summary>
    public void Add(IReadOnlyList<MarkdownSegment> line)
    {
        _lines.Add(line);
        _highlights.Add(null);
        _links.Add(ExtractLinkSpans(line));
    }

    /// <summary>追加代码行（携带该行 token 色段；null = 该行无色段，如空行 — 代码行恒无链接区间）。</summary>
    public void AddCode(IReadOnlyList<MarkdownSegment> line, CodeLineHighlight? highlight)
    {
        _lines.Add(line);
        _highlights.Add(highlight);
        _links.Add([]);
    }

    /// <summary>产出布局结果（行/高亮/链接三表等长对齐）。</summary>
    public MarkdownLayoutResult ToResult() => new(_lines, _highlights, _links);

    /// <summary>行级链接区间提取 — 遍历段累计显示列宽，Url 非空段产物理列半开区间
    ///（相邻同 Url 段不合并 — 各自独立区间，P3 注入时可去重优化；折行拆分段天然各自成区间）。</summary>
    private static IReadOnlyList<LinkSpan> ExtractLinkSpans(IReadOnlyList<MarkdownSegment> line)
    {
        List<LinkSpan>? spans = null;
        var col = 0;
        foreach (var segment in line)
        {
            var width = StringWidth.Of(segment.Text);
            if (segment.Url is { Length: > 0 } url)
            {
                (spans ??= []).Add(new LinkSpan(col, col + width, url));
            }

            col += width;
        }

        return spans ?? [];
    }
}
