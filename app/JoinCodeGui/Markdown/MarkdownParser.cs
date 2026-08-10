using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace JoinCode.Gui.Markdown;

/// <summary>
/// Markdown → 渲染模型 转换器。
/// 用 Markdig 解析 CommonMark（含表格/删除线等扩展），再把 AST 收敛为纯 DTO 模型，
/// 上层渲染不触碰 Markdig 类型。单例管道（线程安全），避免重复构建解析器开销。
/// </summary>
public static class MarkdownParser
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>解析 Markdown 文本为块级渲染模型（空/空白输入返回空列表）</summary>
    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return [];
        }

        var document = Markdig.Markdown.Parse(markdown, Pipeline);
        var blocks = new List<MarkdownBlock>();
        foreach (var block in document)
        {
            if (TryConvertBlock(block, out var converted) && converted is not null)
            {
                blocks.Add(converted);
            }
        }
        return blocks;
    }

    /// <summary>把 Markdig 块转成渲染模型；不支持的块跳过</summary>
    private static bool TryConvertBlock(Block block, out MarkdownBlock? converted)
    {
        converted = block switch
        {
            HeadingBlock heading => new MarkdownHeading(heading.Level, ConvertInlines(heading.Inline)),
            ParagraphBlock paragraph => new MarkdownParagraph(ConvertInlines(paragraph.Inline)),
            FencedCodeBlock code => new MarkdownCodeBlock(code.Info, code.Lines.ToString()),
            ListBlock list => ConvertList(list),
            Markdig.Syntax.QuoteBlock quote => ConvertQuote(quote),
            ThematicBreakBlock => new MarkdownRule(),
            Table table => ConvertTable(table),
            _ => null
        };
        return true;
    }

    /// <summary>列表块 → 有序/无序 + 逐项内联</summary>
    private static MarkdownList ConvertList(ListBlock list)
    {
        var items = new List<IReadOnlyList<MarkdownInline>>();
        foreach (var item in list.OfType<ListItemBlock>())
        {
            var inlines = new List<MarkdownInline>();
            foreach (var child in item.OfType<LeafBlock>())
            {
                if (child.Inline is not null)
                {
                    inlines.AddRange(ConvertInlines(child.Inline));
                }
            }
            items.Add(inlines);
        }
        return new MarkdownList(list.IsOrdered, items);
    }

    /// <summary>引用块 → 内部段落内联合并</summary>
    private static MarkdownQuote ConvertQuote(Markdig.Syntax.QuoteBlock quote)
    {
        var inlines = new List<MarkdownInline>();
        foreach (var child in quote.OfType<LeafBlock>())
        {
            if (child.Inline is not null)
            {
                inlines.AddRange(ConvertInlines(child.Inline));
            }
        }
        return new MarkdownQuote(inlines);
    }

    /// <summary>管道表格 → 表头 + 数据行（单元格取纯文本）</summary>
    private static MarkdownTable ConvertTable(Table table)
    {
        var rows = new List<IReadOnlyList<string>>();
        var header = Array.Empty<string>();
        foreach (var row in table.OfType<TableRow>())
        {
            var cells = row.OfType<TableCell>()
                .Select(cell => ExtractPlainText(cell))
                .ToArray();
            if (row.IsHeader)
            {
                header = cells;
            }
            else if (cells.Length > 0)
            {
                rows.Add(cells);
            }
        }
        return new MarkdownTable(header, rows);
    }

    /// <summary>递归把 Markdig 内联树转成渲染模型内联列表</summary>
    private static IReadOnlyList<MarkdownInline> ConvertInlines(ContainerInline? container)
    {
        var result = new List<MarkdownInline>();
        if (container is null)
        {
            return result;
        }

        foreach (var inline in container)
        {
            result.AddRange(ConvertInline(inline));
        }
        return result;
    }

    /// <summary>单个内联元素 → 渲染模型内联列表（可展开为多个）</summary>
    private static IEnumerable<MarkdownInline> ConvertInline(Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal when literal.Content.IsEmpty:
                yield break;
            case LiteralInline literal:
                yield return new MarkdownText(literal.Content.ToString());
                break;
            case EmphasisInline emphasis when emphasis.DelimiterChar is '*' or '_':
                yield return emphasis.DelimiterCount >= 2
                    ? new MarkdownBold(ConvertInlines(emphasis))
                    : new MarkdownItalic(ConvertInlines(emphasis));
                break;
            case EmphasisInline emphasis when emphasis.DelimiterChar == '~':
                yield return new MarkdownStrikethrough(ConvertInlines(emphasis));
                break;
            case CodeInline code:
                yield return new MarkdownCode(code.Content.ToString());
                break;
            case LinkInline link:
                yield return new MarkdownLink(link.Url ?? string.Empty, ConvertInlines(link));
                break;
            case ContainerInline child:
                foreach (var nested in ConvertInlines(child))
                {
                    yield return nested;
                }
                break;
            default:
                yield break;
        }
    }

    /// <summary>提取一个块的全部内联纯文本（表格单元格用）</summary>
    private static string ExtractPlainText(Block block)
    {
        var sb = new StringBuilder();
        foreach (var leaf in block.Descendants().OfType<LeafBlock>())
        {
            if (leaf.Inline is null)
            {
                continue;
            }
            foreach (var inline in leaf.Inline)
            {
                if (inline is LiteralInline literal && !literal.Content.IsEmpty)
                {
                    sb.Append(literal.Content.ToString());
                }
            }
        }
        return sb.ToString().Trim();
    }
}
