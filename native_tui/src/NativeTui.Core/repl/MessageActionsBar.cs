namespace NativeTui.Core.Repl;

/// <summary>
/// 消息操作底部操作条（M8-6）— 消息光标导航态激活期间挂载于滚动区尾部 spinner 之前
/// （闲时 spinner Hidden 高度 0，操作条即滚动区末行 = 视觉输入框上方；对齐基准 messageActions
/// 底部键位提示条）：margin 1 + dim 全宽 ─ 上边框 + 1 空格缩进 dim 键位提示行（间隔符复用
/// FooterBar 的 MiddotSeparator 先例）。非 ICursorNavigable（不参与导航、不被选中高亮投影）；
/// ReplShell 挂载/卸载（BeginCursorNavigation 进入 / EscapeCursorNavigation 退出）。
/// </summary>
public sealed class MessageActionsBar : IScrollItem
{
    /// <summary>键位提示文案（c 复制全文 / p 复制主参数 — M8-6 新增两键 + 既有导航键全集）。</summary>
    internal const string HintsText =
        $"enter 展开{Figures.MiddotSeparator}c 复制{Figures.MiddotSeparator}p 复制参数"
        + $"{Figures.MiddotSeparator}{Figures.UpArrow}{Figures.DownArrow} 导航{Figures.MiddotSeparator}esc 返回";

    /// <summary>内容高度 = margin 1 + 上边框 1 + 键位提示行 1。</summary>
    public int MeasureHeight(int width) => 3;

    /// <summary>渲染：margin 行留空 → dim 全宽 ─ 上边框 → 1 空格缩进 dim 键位提示行。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        RenderTopBorder(screen, frame with { Y = frame.Y + 1, Height = 1 }, dim);
        screen.WriteText(frame.Y + 2, frame.X, $" {HintsText}", dim, frame.Right);
    }

    /// <summary>dim 全宽上边框（─ 逐格填充，对齐权限对话框边框形态）。</summary>
    private static void RenderTopBorder(Screen screen, Rect row, int dim)
    {
        for (var col = row.X; col < row.Right; col++)
        {
            screen[row.Y, col] = new Cell(new Rune('─'), dim, CellWidth.Default);
        }
    }
}

/// <summary>
/// 消息复制文本提取器（M8-6）— 光标导航态 c（全文）/ p（主参数）按消息项类型取文本
/// （基准 PRIMARY_INPUT 按工具名映射 path/command/pattern 的本地简化）：
/// 用户/助手/错误/工具结果取 Text；diff 取 Summary（p）/ Summary + 补丁全量（c）；
/// 工具调用经无损渲染行提取（toolName 与 args 无公开属性 — MessageItems.cs 并行冻结禁改，
/// 超宽单行屏渲染后剥前缀：c = "工具名(参数)"、p = 括号内参数）；Markdown 递归块拼接。
/// 不可提取类型（子代理分组/权限对话框等）返回 null（调用方静默降级 no-op）。
/// </summary>
internal static class MessageActionsCopy
{
    /// <summary>工具调用渲染提取屏宽度（参数无损上限 — 超长截断对齐渲染裁剪语义）。</summary>
    private const int ExtractWidth = 4096;

    /// <summary>按消息项类型提取复制文本（primaryOnly=true 取主参数 p / false 取全文 c；null = 不可复制）。</summary>
    public static string? TryGetText(IScrollItem item, bool primaryOnly) => item switch
    {
        UserMessageItem user => user.Text,
        AssistantMessageItem assistant => assistant.Text,
        ErrorMessageItem error => error.Text,
        ToolResultItem result => result.Text,
        DiffResultItem diff => primaryOnly ? diff.Summary : DiffFullText(diff),
        ToolCallItem tool => primaryOnly ? ToolCallArgs(tool) : ToolCallLine(tool),
        MarkdownMessageItem markdown => MarkdownFullText(markdown),
        _ => null,
    };

    /// <summary>diff 全文 = Summary + 各 hunk（头 + 前缀行 — 渲染同构文本形态）。</summary>
    private static string DiffFullText(DiffResultItem diff)
    {
        var builder = new StringBuilder(diff.Summary);
        foreach (var hunk in diff.Patch)
        {
            builder.Append('\n').Append(hunk.Header);
            foreach (var line in hunk.Lines)
            {
                builder.Append('\n').Append(line.Kind switch
                {
                    DiffLineKind.Added => "+",
                    DiffLineKind.Removed => "-",
                    _ => " ",
                }).Append(line.Content);
            }
        }

        return builder.ToString();
    }

    /// <summary>工具调用主参数 — 渲染行剥 ● 前缀 + 工具名 + 括号（格式异常防御回退整行参数段）。</summary>
    private static string? ToolCallArgs(ToolCallItem item)
    {
        var line = ToolCallLine(item);
        if (line is null)
        {
            return null;
        }

        var open = line.IndexOf('(');
        if (open <= 0 || line[^1] != ')')
        {
            return line;
        }

        return line[(open + 1)..^1];
    }

    /// <summary>工具调用渲染行提取（"● 工具名(参数)" — 剥 ● 前缀后全文形态）。</summary>
    private static string? ToolCallLine(ToolCallItem item)
    {
        var rendered = RenderToolCallRow(item);
        return rendered is { Length: > 2 } && rendered.StartsWith($"{Figures.MessageBullet} ", StringComparison.Ordinal)
            ? rendered[2..]
            : rendered;
    }

    /// <summary>无损渲染工具调用行 — 超宽两行屏（参数不截断；行 0 = item 自带 margin 留空，
    /// 行 1 = 正文）渲染后提取正文行纯文本（样式无关：零宽字符跳过/宽字符占位尾格跳过由
    /// WriteText 与提取共同保证）。</summary>
    private static string? RenderToolCallRow(ToolCallItem item)
    {
        using var screen = new Screen(ExtractWidth, 2);
        item.Render(screen, new Rect(0, 0, ExtractWidth, 2), Theme.Dark);
        return RowPlainText(screen, 1);
    }

    /// <summary>行纯文本提取 — 尾部空格列截断 + 宽字符占位尾格跳过；全空行返回 null。</summary>
    private static string? RowPlainText(Screen screen, int row)
    {
        var last = -1;
        for (var col = screen.Cols - 1; col >= 0; col--)
        {
            if (screen[row, col].Value != new Rune(' '))
            {
                last = col;
                break;
            }
        }

        if (last < 0)
        {
            return null;
        }

        var builder = new StringBuilder();
        for (var col = 0; col <= last; col++)
        {
            var cell = screen[row, col];
            if (cell.Width == CellWidth.SpacerTail)
            {
                continue;
            }

            builder.Append(cell.Value.ToString());
        }

        return builder.ToString();
    }

    /// <summary>Markdown 全文 — 块递归拼接（段/标题段序列、代码原文、引用与列表项递归、
    /// 分隔线、表格单元格 | 连接），顶层块间换行。</summary>
    private static string? MarkdownFullText(MarkdownMessageItem item)
    {
        var builder = new StringBuilder();
        foreach (var block in item.Document.Blocks)
        {
            AppendBlock(builder, block);
            builder.Append('\n');
        }

        if (builder.Length == 0)
        {
            return null;
        }

        var text = builder.ToString().TrimEnd('\n');
        return text.Length == 0 ? null : text;
    }

    /// <summary>单块文本追加（嵌套块递归）。</summary>
    private static void AppendBlock(StringBuilder builder, MarkdownBlock block)
    {
        switch (block)
        {
            case MarkdownParagraph paragraph:
                AppendSegments(builder, paragraph.Segments);
                break;
            case MarkdownHeading heading:
                AppendSegments(builder, heading.Segments);
                break;
            case MarkdownCode code:
                builder.Append(code.Text);
                break;
            case MarkdownQuote quote:
                AppendChildBlocks(builder, quote.Children);
                break;
            case MarkdownList list:
                AppendListItems(builder, list);
                break;
            case MarkdownRule:
                builder.Append("---");
                break;
            case MarkdownTable table:
                AppendTable(builder, table);
                break;
        }
    }

    /// <summary>子块序列追加（块间换行 — 引用/列表项子块）。</summary>
    private static void AppendChildBlocks(StringBuilder builder, IReadOnlyList<MarkdownBlock> children)
    {
        for (var i = 0; i < children.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            AppendBlock(builder, children[i]);
        }
    }

    /// <summary>列表项追加（无序 "- " / 有序 "N. " 前缀 + 项内子块递归）。</summary>
    private static void AppendListItems(StringBuilder builder, MarkdownList list)
    {
        for (var i = 0; i < list.Items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append('\n');
            }

            builder.Append(list.Ordered ? $"{list.Start + i}. " : "- ");
            AppendChildBlocks(builder, list.Items[i].Children);
        }
    }

    /// <summary>表格追加（表头 + 数据行，单元格 | 连接 — 渲染同构文本形态）。</summary>
    private static void AppendTable(StringBuilder builder, MarkdownTable table)
    {
        AppendRow(builder, table.Headers);
        foreach (var row in table.Rows)
        {
            builder.Append('\n');
            AppendRow(builder, row);
        }
    }

    /// <summary>单行追加（单元格 | 连接，每单元格为段序列）。</summary>
    private static void AppendRow(StringBuilder builder, IReadOnlyList<IReadOnlyList<MarkdownSegment>> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(" | ");
            }

            AppendSegments(builder, cells[i]);
        }
    }

    /// <summary>行内段序列追加（拼接段文本）。</summary>
    private static void AppendSegments(StringBuilder builder, IReadOnlyList<MarkdownSegment> segments)
    {
        foreach (var segment in segments)
        {
            builder.Append(segment.Text);
        }
    }
}
