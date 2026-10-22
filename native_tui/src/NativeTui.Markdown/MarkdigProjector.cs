namespace NativeTui.Markdown;

/// <summary>
/// Markdig 投影器（M7-4）— Markdig AST → NativeTui.Core 块模型（ADR 0140 卫星层：
/// Markdig 依赖隔离在本项目，Core 保持零依赖；DTO 主题/宽度无关，渲染决策全部后置到 Core 布局层）。
/// 投影只产出结构 + 行内样式 flags（标题级别样式由布局层施加）；
/// 删除线忽略（基准 configureMarked 禁用 del 解析，~ 常用于"约等于"）；
/// html 块/链接引用定义丢弃（基准 def/del/html 不渲染）；
/// 链接 mailto 降级纯文本、图片显示 URL（基准 formatToken link/image）。
/// </summary>
public static class MarkdigProjector
{
    /// <summary>解析管线 — UseAdvancedExtensions（表格/自动链接等 GFM 扩展，对齐青蛙TUI 先例）。</summary>
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>
    /// 快路径投影 — 无 Markdown 语法字符返回 null（纯文本降级渲染），有则投影文档
    ///（对齐基准 hasMarkdownSyntax：前 500 字符采样检查，短回复跳过 ~3ms 解析成本）。
    /// </summary>
    public static MarkdownDocument? ProjectOptional(string text) =>
        HasMarkdownSyntax(text) ? Project(text) : null;

    /// <summary>Markdown 源文本 → Core 块模型文档。</summary>
    public static MarkdownDocument Project(string markdown) =>
        new(ProjectBlocks(Markdig.Markdown.Parse(markdown, Pipeline)));

    /// <summary>已解析 Markdig 文档 → Core 块模型投影（internal — 流式投影器共享同一分派，零重复）。</summary>
    internal static IReadOnlyList<MarkdownBlock> ProjectBlocks(Markdig.Syntax.MarkdownDocument document) =>
        document.Select(ProjectBlock).OfType<MarkdownBlock>().ToList();

    /// <summary>共享解析管线（internal — 流式投影器块边界探测复用，UseAdvancedExtensions 一致解析语义）。</summary>
    internal static MarkdownPipeline SharedPipeline => Pipeline;

    /// <summary>
    /// Markdown 语法标记检测（internal — 流式投影器无语法快路径共享；基准 MD_SYNTAX_RE 移植：
    /// 任意位置语法字符 / 空行 / 行首有序列表，采样前 500 字符）。
    /// </summary>
    internal static bool HasMarkdownSyntax(string text)
    {
        var sample = text.Length > 500 ? text.AsSpan(0, 500) : text.AsSpan();
        foreach (var current in sample)
        {
            if (current is '#' or '*' or '`' or '[' or '|' or '>' or '~' or '-' or '_')
            {
                return true;
            }
        }

        if (sample.IndexOf("\n\n".AsSpan(), StringComparison.Ordinal) >= 0)
        {
            return true;
        }

        var lineStart = 0;
        for (var index = 0; index <= sample.Length; index++)
        {
            if (index < sample.Length && sample[index] != '\n')
            {
                continue;
            }

            if (HasOrderedPrefix(sample[lineStart..index]))
            {
                return true;
            }

            lineStart = index + 1;
        }

        return false;
    }

    /// <summary>行首有序列表前缀 "N. " 检测。</summary>
    private static bool HasOrderedPrefix(ReadOnlySpan<char> line)
    {
        var digits = 0;
        while (digits < line.Length && char.IsDigit(line[digits]))
        {
            digits++;
        }

        return digits > 0 && digits + 1 < line.Length && line[digits] == '.' && line[digits + 1] == ' ';
    }

    /// <summary>块投影分派 — 未覆盖的块类型丢弃（返回 null）。</summary>
    private static MarkdownBlock? ProjectBlock(Block block) => block switch
    {
        HeadingBlock heading => new MarkdownHeading(heading.Level, ProjectInlines(heading.Inline, MarkdownStyle.None)),
        ParagraphBlock paragraph => new MarkdownParagraph(ProjectInlines(paragraph.Inline, MarkdownStyle.None)),
        FencedCodeBlock code => new MarkdownCode(code.Info ?? string.Empty, code.Lines.ToString()),
        CodeBlock code => new MarkdownCode(string.Empty, code.Lines.ToString()),
        QuoteBlock quote => new MarkdownQuote(quote.Select(ProjectBlock).OfType<MarkdownBlock>().ToList()),
        ListBlock list => ProjectList(list),
        ThematicBreakBlock => new MarkdownRule(),
        Table table => ProjectTable(table),
        _ => null,
    };

    /// <summary>列表投影 — 有序列表保留首项 Order 为 Start（基准 token.start）。</summary>
    private static MarkdownBlock ProjectList(ListBlock list)
    {
        var items = list.OfType<ListItemBlock>()
            .Select(item => new MarkdownListItem(item.Select(ProjectBlock).OfType<MarkdownBlock>().ToList()))
            .ToList();
        return new MarkdownList(list.IsOrdered, OrderedStart(list), items);
    }

    /// <summary>有序列表起始编号（首个 Order&gt;0 的项；无序或缺失回退 1）。</summary>
    private static int OrderedStart(ListBlock list) =>
        list.OfType<ListItemBlock>().FirstOrDefault(static item => item.Order > 0)?.Order ?? 1;

    /// <summary>表格投影 — IsHeader 行为表头，其余为数据行（Markdig TableRow.IsHeader）。</summary>
    private static MarkdownBlock ProjectTable(Table table)
    {
        var rows = table.OfType<TableRow>().ToList();
        var headers = rows.Where(static row => row.IsHeader).Select(ProjectRow).FirstOrDefault() ?? [];
        var data = rows.Where(static row => !row.IsHeader).Select(ProjectRow).ToList();
        return new MarkdownTable(headers, data);
    }

    /// <summary>表格行投影 — 行 → 格列表（每格 = 其内 leaf 块行内段的顺序拼接，对齐青蛙TUI CellText 拼接先例）。</summary>
    private static IReadOnlyList<IReadOnlyList<MarkdownSegment>> ProjectRow(TableRow row) =>
        row.OfType<TableCell>()
            .Select(cell => ProjectInlines(cell.OfType<LeafBlock>().SelectMany(static leaf => EnumerateInlines(leaf.Inline)), MarkdownStyle.None))
            .ToList();

    /// <summary>行内投影 — 序列递归 + 样式 flags 叠加（基准 inherited 语义；ContainerInline 即 IEnumerable&lt;Inline&gt;）。</summary>
    private static IReadOnlyList<MarkdownSegment> ProjectInlines(IEnumerable<Inline>? inlines, MarkdownStyle inherited)
    {
        var segments = new List<MarkdownSegment>();
        foreach (var inline in inlines ?? [])
        {
            ProjectInline(inline, inherited, segments);
        }

        return segments;
    }

    /// <summary>行内分派投影（各分支独立成方法）。</summary>
    private static void ProjectInline(Inline inline, MarkdownStyle inherited, List<MarkdownSegment> segments)
    {
        switch (inline)
        {
            case LiteralInline literal:
                AppendLiteral(segments, literal, inherited);
                break;
            case CodeInline code:
                segments.Add(new MarkdownSegment(code.Content, inherited | MarkdownStyle.Code));
                break;
            case EmphasisInline emphasis:
                ProjectEmphasis(emphasis, inherited, segments);
                break;
            case LinkInline link:
                ProjectLink(link, inherited, segments);
                break;
            case AutolinkInline autolink:
                segments.Add(new MarkdownSegment(autolink.Url, MarkdownStyle.Link, autolink.Url));
                break;
            case LineBreakInline:
                // 硬换行首版丢弃（对齐青蛙TUI GUI 先例；Core 段落模型无段内断行概念，后续按需扩展）
                break;
            case ContainerInline nested:
                ProjectInlinesInto(nested, inherited, segments);
                break;
            default:
                if (inline.ToString() is { Length: > 0 } fallback)
                {
                    segments.Add(new MarkdownSegment(fallback, inherited));
                }

                break;
        }
    }

    /// <summary>强调投影 — DelimiterCount>=2 为粗体、单个为斜体；'~' 删除线忽略（基准禁用 del）。</summary>
    private static void ProjectEmphasis(EmphasisInline emphasis, MarkdownStyle inherited, List<MarkdownSegment> segments)
    {
        if (emphasis.DelimiterChar == '~')
        {
            ProjectInlinesInto(emphasis, inherited, segments);
            return;
        }

        var style = inherited | (emphasis.DelimiterCount >= 2 ? MarkdownStyle.Bold : MarkdownStyle.Italic);
        ProjectInlinesInto(emphasis, style, segments);
    }

    /// <summary>链接投影 — 图片显示 URL / mailto 降级纯文本 / 其余显示文本携带 Link 标签
    /// + Url 载体（DSG043 P1：递归完成后子段区间回填 Url — 链接内嵌套强调时每子段独立携带
    /// 同一 Url，布局层折行拆分后区间仍可溯源；OSC 8 渲染注入后续批次消费）。</summary>
    private static void ProjectLink(LinkInline link, MarkdownStyle inherited, List<MarkdownSegment> segments)
    {
        if (link.IsImage)
        {
            segments.Add(new MarkdownSegment(link.Url ?? string.Empty, MarkdownStyle.None));
            return;
        }

        if (link.Url?.StartsWith("mailto:", StringComparison.Ordinal) == true)
        {
            ProjectInlinesInto(link, MarkdownStyle.None, segments);
            return;
        }

        var start = segments.Count;
        ProjectInlinesInto(link, inherited | MarkdownStyle.Link, segments);
        var url = link.Url;
        if (url is { Length: > 0 })
        {
            for (var i = start; i < segments.Count; i++)
            {
                if (segments[i].Style.HasFlag(MarkdownStyle.Link))
                {
                    segments[i] = segments[i] with { Url = url };
                }
            }
        }
    }

    /// <summary>容器行内递归投影（共享目标段列表）。</summary>
    private static void ProjectInlinesInto(ContainerInline container, MarkdownStyle inherited, List<MarkdownSegment> segments)
    {
        foreach (var inline in container)
        {
            ProjectInline(inline, inherited, segments);
        }
    }

    /// <summary>字面量段 — 与前段同样式时合并（消除碎片段）。</summary>
    private static void AppendLiteral(List<MarkdownSegment> segments, LiteralInline literal, MarkdownStyle inherited)
    {
        var text = literal.Content.ToString();
        if (text.Length == 0)
        {
            return;
        }

        if (segments.Count > 0 && segments[^1].Style == inherited)
        {
            segments[^1] = segments[^1] with { Text = segments[^1].Text + text };
            return;
        }

        segments.Add(new MarkdownSegment(text, inherited));
    }

    /// <summary>容器展开为行内序列（表格单元格多 leaf 块拼接用）。</summary>
    private static IEnumerable<Inline> EnumerateInlines(ContainerInline? container)
    {
        if (container is null)
        {
            yield break;
        }

        foreach (var inline in container)
        {
            yield return inline;
        }
    }
}
