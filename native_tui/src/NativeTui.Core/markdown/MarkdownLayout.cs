namespace NativeTui.Core.Markdown;

/// <summary>
/// Markdown 布局器 — 块模型 → 物理行模型（每行 = 段序列），对齐基准 formatToken：
/// 段落软换行（词边界贪心 + CJK 逐字，续行带 continuationPrefix）；
/// 标题 h1=Bold|Italic|Underline、h2+=Bold；引用每行 ▎ dim 前缀 + 内容叠 Italic
///（修复青蛙TUI 前缀只加一次的坑）；列表 2 空格/层缩进 + "- "/"N. " 前缀
///（有序深度分级：0/1 数字、2 字母、3 罗马数字；项内续行对齐内容列，嵌套列表自带缩进前缀）；
/// 代码块原文逐行；hr = "---"；表格列宽 = max(显示宽, 3) + "|" 分隔；
/// 顶层块间空 1 行（基准 Box gap 视觉主路径），引用/列表内部子块连续不空行。
/// </summary>
public static class MarkdownLayout
{
    /// <summary>引用条前缀（基准 BLOCKQUOTE_BAR + 空格）。</summary>
    private const string QuotePrefix = "▎ ";

    /// <summary>列表缩进宽度（列/层，基准 '  '.repeat(listDepth)）。</summary>
    private const int ListIndentCols = 2;

    /// <summary>块模型 → 物理行列表（无高亮 — 旧入口兼容既有调用方；高亮场景走 LayoutWithHighlights）。</summary>
    public static IReadOnlyList<IReadOnlyList<MarkdownSegment>> Layout(MarkdownDocument document, int width) =>
        LayoutWithHighlights(document, width, null).Lines;

    /// <summary>块模型 → 布局结果（行 + 逐行代码高亮；highlighter null = 高亮禁用降级纯文本）。</summary>
    public static MarkdownLayoutResult LayoutWithHighlights(MarkdownDocument document, int width, ICodeHighlighter? highlighter)
    {
        var lines = new LayoutLines(highlighter);
        AppendBlocks(document.Blocks, Math.Max(0, width), [], [], MarkdownStyle.None, lines, gapBetweenBlocks: true);
        return lines.ToResult();
    }

    /// <summary>追加块序列 — 顶层块间空 1 行，引用/列表内子块连续（gapBetweenBlocks 由调用方决定）。</summary>
    private static void AppendBlocks(
        IReadOnlyList<MarkdownBlock> blocks,
        int width,
        IReadOnlyList<MarkdownSegment> prefix,
        IReadOnlyList<MarkdownSegment> continuationPrefix,
        MarkdownStyle extraStyle,
        LayoutLines lines,
        bool gapBetweenBlocks)
    {
        var first = true;
        foreach (var block in blocks)
        {
            if (gapBetweenBlocks && !first)
            {
                lines.Add([]);
            }

            AppendBlock(block, width, prefix, continuationPrefix, extraStyle, lines);
            first = false;
        }
    }

    /// <summary>按块类型分派（各分支独立成方法）。</summary>
    private static void AppendBlock(
        MarkdownBlock block,
        int width,
        IReadOnlyList<MarkdownSegment> prefix,
        IReadOnlyList<MarkdownSegment> continuationPrefix,
        MarkdownStyle extraStyle,
        LayoutLines lines)
    {
        switch (block)
        {
            case MarkdownParagraph paragraph:
                AppendParagraph(paragraph, width, prefix, continuationPrefix, extraStyle, lines);
                break;
            case MarkdownHeading heading:
                AppendParagraph(AsHeadingParagraph(heading), width, prefix, continuationPrefix, extraStyle, lines);
                break;
            case MarkdownQuote quote:
                AppendQuote(quote, width, prefix, continuationPrefix, extraStyle, lines);
                break;
            case MarkdownList list:
                AppendList(list, width, prefix, extraStyle, lines);
                break;
            case MarkdownCode code:
                AppendCode(code, prefix, lines);
                break;
            case MarkdownRule:
                AppendRule(prefix, lines);
                break;
            case MarkdownTable table:
                AppendTable(table, prefix, lines);
                break;
            default:
                lines.Add([]);
                break;
        }
    }

    /// <summary>标题转等效段落 — 级别样式并入段 flags（h1 三修饰，h2+ 仅 Bold）。</summary>
    private static MarkdownParagraph AsHeadingParagraph(MarkdownHeading heading)
    {
        var headingStyle = heading.Level == 1
            ? MarkdownStyle.Bold | MarkdownStyle.Italic | MarkdownStyle.Underline
            : MarkdownStyle.Bold;
        var segments = heading.Segments.Select(segment => segment with { Style = segment.Style | headingStyle }).ToArray();
        return new MarkdownParagraph(segments);
    }

    /// <summary>段落 — 软换行到剩余宽度：首行打 prefix，续行打 continuationPrefix（列表项对齐内容列）。</summary>
    private static void AppendParagraph(
        MarkdownParagraph paragraph,
        int width,
        IReadOnlyList<MarkdownSegment> prefix,
        IReadOnlyList<MarkdownSegment> continuationPrefix,
        MarkdownStyle extraStyle,
        LayoutLines lines)
    {
        var prefixWidth = SegmentsWidth(prefix);
        var segments = ApplyStyle(paragraph.Segments, extraStyle);
        var wrapped = WrapSegments(segments, Math.Max(1, width - prefixWidth)).ToList();
        for (var index = 0; index < wrapped.Count; index++)
        {
            var line = new List<MarkdownSegment>(index == 0 ? prefix : continuationPrefix);
            line.AddRange(wrapped[index]);
            lines.Add(line);
        }
    }

    /// <summary>引用 — 每行加 ▎ dim 前缀 + 内容叠 Italic；子块连续（基准 quote inner 直接拼接）。</summary>
    private static void AppendQuote(
        MarkdownQuote quote,
        int width,
        IReadOnlyList<MarkdownSegment> prefix,
        IReadOnlyList<MarkdownSegment> continuationPrefix,
        MarkdownStyle extraStyle,
        LayoutLines lines)
    {
        var quotePrefix = AppendSegment(prefix, new MarkdownSegment(QuotePrefix, MarkdownStyle.Dim));
        var innerWidth = Math.Max(0, width - SegmentsWidth(quotePrefix));
        AppendBlocks(quote.Children, innerWidth, quotePrefix, quotePrefix, extraStyle | MarkdownStyle.Italic, lines, gapBetweenBlocks: false);
    }

    /// <summary>列表 — 项首块用完整标记前缀，后续子块（嵌套列表/引用等）从项内容列起。</summary>
    private static void AppendList(
        MarkdownList list,
        int width,
        IReadOnlyList<MarkdownSegment> prefix,
        MarkdownStyle extraStyle,
        LayoutLines lines)
    {
        var indentWidth = SegmentsWidth(prefix);
        for (var index = 0; index < list.Items.Count; index++)
        {
            var marker = list.Ordered
                ? $"{ListNumber(list.Start + index, DepthOf(prefix))}. "
                : "- ";
            AppendListItem(list.Items[index], width, indentWidth, marker, extraStyle, lines);
        }
    }

    /// <summary>列表项 — 首块首行带 indent+marker 前缀、续行对齐 markerCol；后续块（嵌套列表等）从内容列起。</summary>
    private static void AppendListItem(
        MarkdownListItem item,
        int width,
        int indentWidth,
        string marker,
        MarkdownStyle extraStyle,
        LayoutLines lines)
    {
        var markerPrefix = new List<MarkdownSegment> { new(new string(' ', indentWidth) + marker, MarkdownStyle.None) };
        var continuationPrefix = new List<MarkdownSegment> { new(new string(' ', indentWidth + marker.Length), MarkdownStyle.None) };

        var first = true;
        foreach (var child in item.Children)
        {
            var childPrefix = first ? markerPrefix : continuationPrefix;
            var childContinuation = first && child is MarkdownParagraph or MarkdownHeading
                ? continuationPrefix
                : childPrefix;
            AppendBlock(child, width, childPrefix, childContinuation, extraStyle, lines);
            first = false;
        }
    }

    /// <summary>代码块 — 原文逐行（不换行，超宽由 Screen 裁剪），前缀段打头；高亮器在位时整块高亮（跨行语法状态）。</summary>
    private static void AppendCode(MarkdownCode code, IReadOnlyList<MarkdownSegment> prefix, LayoutLines lines)
    {
        var text = code.Text.TrimEnd('\n');
        if (text.Length == 0)
        {
            return;
        }

        var codeLines = text.Split('\n');
        var highlights = HighlightCodeBlock(lines.Highlighter, code.Language, codeLines, SegmentsWidth(prefix));
        for (var index = 0; index < codeLines.Length; index++)
        {
            var line = new List<MarkdownSegment>(prefix) { new(codeLines[index], MarkdownStyle.None) };
            lines.AddCode(line, highlights?[index]);
        }
    }

    /// <summary>整块高亮 → 逐行色段（Start 平移前缀宽度，坐标系 = 布局行完整段文本；null = 高亮器缺席/语言不支持）。</summary>
    private static IReadOnlyList<CodeLineHighlight?>? HighlightCodeBlock(
        ICodeHighlighter? highlighter,
        string language,
        string[] codeLines,
        int prefixWidth)
    {
        var spans = highlighter?.Highlight(language, codeLines);
        if (spans is null)
        {
            return null;
        }

        return spans.Select(lineSpans => new CodeLineHighlight(
            lineSpans
                .Where(span => span.Length > 0)
                .Select(span => span with { Start = span.Start + prefixWidth })
                .ToArray())).ToArray();
    }

    /// <summary>分隔线 — "---" 三字符（基准 hr）。</summary>
    private static void AppendRule(IReadOnlyList<MarkdownSegment> prefix, LayoutLines lines)
    {
        var line = new List<MarkdownSegment>(prefix) { new("---", MarkdownStyle.None) };
        lines.Add(line);
    }

    /// <summary>表格 — 列宽 = max(显示宽, 3)；表头/数据行 "| x | y |"（末格 trimEnd 语义），分隔行 "|---|---|"。</summary>
    private static void AppendTable(MarkdownTable table, IReadOnlyList<MarkdownSegment> prefix, LayoutLines lines)
    {
        var columnCount = table.Headers.Count;
        if (columnCount == 0)
        {
            return;
        }

        var widths = Enumerable.Range(0, columnCount)
            .Select(column => Math.Max(3, CellDisplayWidth(table, column)))
            .ToArray();

        lines.Add(TableRow(prefix, table.Headers, widths));
        lines.Add(new List<MarkdownSegment>(prefix) { new(TableSeparator(widths), MarkdownStyle.None) });
        foreach (var row in table.Rows)
        {
            lines.Add(TableRow(prefix, row, widths));
        }
    }

    /// <summary>表格数据行 — "| " + pad(单元格段) + " "（末格 " |"，对齐基准行尾 trimEnd）。</summary>
    private static List<MarkdownSegment> TableRow(
        IReadOnlyList<MarkdownSegment> prefix,
        IReadOnlyList<IReadOnlyList<MarkdownSegment>> cells,
        int[] widths)
    {
        var line = new List<MarkdownSegment>(prefix);
        for (var column = 0; column < widths.Length; column++)
        {
            var cell = column < cells.Count ? cells[column] : [];
            var padding = widths[column] - SegmentsWidth(cell);
            line.Add(new MarkdownSegment("| ", MarkdownStyle.None));
            line.AddRange(cell);
            if (padding > 0)
            {
                line.Add(new MarkdownSegment(new string(' ', padding), MarkdownStyle.None));
            }

            line.Add(new MarkdownSegment(column == widths.Length - 1 ? " |" : " ", MarkdownStyle.None));
        }

        return line;
    }

    /// <summary>表格分隔行 "|------|---------|"（每列 w+2 个 dash）。</summary>
    private static string TableSeparator(int[] widths) =>
        string.Concat(widths.Select(width => $"|{new string('-', width + 2)}")) + "|";

    /// <summary>列显示宽 = 表头与所有数据行该列的最大显示宽。</summary>
    private static int CellDisplayWidth(MarkdownTable table, int column) =>
        Math.Max(SegmentsWidth(table.Headers[column]), table.Rows.Max(row => SegmentsWidth(row[column])));

    /// <summary>有序列表分级编号（基准 getListNumber：深度 0/1 数字、2 字母、3 罗马数字）。</summary>
    private static string ListNumber(int number, int depth) => depth switch
    {
        2 => ToLetters(number),
        3 => ToRoman(number),
        _ => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>数字转字母编号（a、b、…、aa）。</summary>
    private static string ToLetters(int number)
    {
        var result = "";
        while (number > 0)
        {
            number--;
            result = (char)('a' + number % 26) + result;
            number /= 26;
        }

        return result;
    }

    /// <summary>数字转小写罗马数字。</summary>
    private static string ToRoman(int number)
    {
        var result = "";
        foreach (var (value, numeral) in new[] { (1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i") })
        {
            while (number >= value)
            {
                result += numeral;
                number -= value;
            }
        }

        return result;
    }

    /// <summary>按前缀段数推断列表深度（每段约 1 层；3 层+ 嵌套为边缘场景，首版近似）。</summary>
    private static int DepthOf(IReadOnlyList<MarkdownSegment> prefix) => prefix.Count;

    /// <summary>段落样式叠加（引用 Italic 等容器样式向行内传递，基准 inherited 语义）。</summary>
    private static IReadOnlyList<MarkdownSegment> ApplyStyle(IReadOnlyList<MarkdownSegment> segments, MarkdownStyle extraStyle) =>
        extraStyle == MarkdownStyle.None
            ? segments
            : segments.Select(segment => segment with { Style = segment.Style | extraStyle }).ToArray();

    /// <summary>前缀段链尾部追加一段。</summary>
    private static List<MarkdownSegment> AppendSegment(IReadOnlyList<MarkdownSegment> prefix, MarkdownSegment segment)
    {
        var result = new List<MarkdownSegment>(prefix) { segment };
        return result;
    }

    /// <summary>段序列软换行 — 词边界贪心断行 + CJK 逐字断；相邻同样式 token 合并回段。</summary>
    private static IEnumerable<List<MarkdownSegment>> WrapSegments(IReadOnlyList<MarkdownSegment> segments, int width)
    {
        var lines = new List<List<MarkdownSegment>>();
        var current = new List<(string Text, MarkdownStyle Style, string? Url)>();
        var currentWidth = 0;
        var pendingSpace = 0;

        foreach (var token in Tokenize(segments))
        {
            if (token.Text.Length == 0)
            {
                continue;
            }

            if (token.Text[0] == ' ')
            {
                pendingSpace += token.Text.Length;
                continue;
            }

            var tokenWidth = StringWidth.Of(token.Text);
            if (currentWidth > 0 && currentWidth + pendingSpace + tokenWidth > width)
            {
                FlushLine();
            }

            if (pendingSpace > 0 && current.Count > 0)
            {
                current.Add((new string(' ', pendingSpace), current[^1].Style, current[^1].Url));
            }

            pendingSpace = 0;
            if (tokenWidth <= width)
            {
                current.Add((token.Text, token.Style, token.Url));
                currentWidth += tokenWidth;
                continue;
            }

            // 超宽 token（无空格长串）：逐 rune 硬断
            foreach (var hard in HardSplit(token, width))
            {
                var hardWidth = StringWidth.Of(hard.Text);
                if (currentWidth > 0 && currentWidth + hardWidth > width)
                {
                    FlushLine();
                }

                current.Add((hard.Text, hard.Style, hard.Url));
                currentWidth += hardWidth;
            }
        }

        FlushLine();
        return lines;

        void FlushLine()
        {
            if (current.Count > 0)
            {
                lines.Add(MergeSegments(current));
            }

            current = [];
            currentWidth = 0;
            pendingSpace = 0;
        }
    }

    /// <summary>段序列 → token 流（空格串 / ASCII 词 / 宽 rune 单字，样式与 Url 随段携带
    /// — DSG043 P2：链接 Url 穿透折行层，行段区间提取的数据前提）。</summary>
    private static IEnumerable<(string Text, MarkdownStyle Style, string? Url)> Tokenize(IReadOnlyList<MarkdownSegment> segments)
    {
        foreach (var segment in segments)
        {
            var run = new System.Text.StringBuilder();
            foreach (var rune in segment.Text.EnumerateRunes())
            {
                if (rune.Value == ' ')
                {
                    if (run.Length > 0)
                    {
                        yield return (run.ToString(), segment.Style, segment.Url);
                        run.Clear();
                    }

                    run.Append(' ');
                    continue;
                }

                if (run.Length > 0 && run[0] == ' ')
                {
                    yield return (run.ToString(), segment.Style, segment.Url);
                    run.Clear();
                }

                if (RuneWidth.Of(rune) >= 2)
                {
                    if (run.Length > 0)
                    {
                        yield return (run.ToString(), segment.Style, segment.Url);
                        run.Clear();
                    }

                    yield return (rune.ToString(), segment.Style, segment.Url);
                    continue;
                }

                run.Append(rune);
            }

            if (run.Length > 0)
            {
                yield return (run.ToString(), segment.Style, segment.Url);
            }
        }
    }

    /// <summary>超宽 token 逐 rune 硬断（每片 ≤ width — Url 随片携带：折行拆分后每片独立链接段）。</summary>
    private static IEnumerable<(string Text, MarkdownStyle Style, string? Url)> HardSplit((string Text, MarkdownStyle Style, string? Url) token, int width)
    {
        var sliceWidth = 0;
        var slice = new System.Text.StringBuilder();
        foreach (var rune in token.Text.EnumerateRunes())
        {
            var runeWidth = RuneWidth.Of(rune);
            if (sliceWidth + runeWidth > width && slice.Length > 0)
            {
                yield return (slice.ToString(), token.Style, token.Url);
                slice.Clear();
                sliceWidth = 0;
            }

            slice.Append(rune);
            sliceWidth += runeWidth;
        }

        if (slice.Length > 0)
        {
            yield return (slice.ToString(), token.Style, token.Url);
        }
    }

    /// <summary>相邻同样式 token 合并回段（消除碎片段）— 合并键 Style + Url
    ///（DSG043 P2：不同 Url 的链接段不合并防链接归属丢失；同 Url 拆片段合并后区间提取仍正确）。</summary>
    private static List<MarkdownSegment> MergeSegments(List<(string Text, MarkdownStyle Style, string? Url)> tokens)
    {
        var segments = new List<MarkdownSegment>();
        foreach (var (text, style, url) in tokens)
        {
            if (segments.Count > 0 && segments[^1].Style == style && segments[^1].Url == url)
            {
                segments[^1] = segments[^1] with { Text = segments[^1].Text + text };
                continue;
            }

            segments.Add(new MarkdownSegment(text, style, url));
        }

        return segments;
    }

    /// <summary>段序列显示宽求和。</summary>
    private static int SegmentsWidth(IReadOnlyList<MarkdownSegment> segments) =>
        segments.Sum(static segment => StringWidth.Of(segment.Text));
}
