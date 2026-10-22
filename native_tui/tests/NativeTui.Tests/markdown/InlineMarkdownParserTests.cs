namespace NativeTui.Tests.Markdown;

/// <summary>
/// 行内 Markdown 轻量解析器测试（TASK064/TASK065 P3）—
/// 三件套标记（**bold** / *italic* / `code` / ***粗斜体***）、字面降级契约
///（未闭合标记 / snake_case 下划线 / 纯文本 → 空列表）、多段顺序与平级不递归语义。
/// </summary>
public sealed class InlineMarkdownParserTests
{
    /// <summary>解析辅助（string → span 入口）。</summary>
    private static IReadOnlyList<MarkdownSegment> Parse(string line) => InlineMarkdownParser.ParseLine(line);

    /// <summary>纯文本 — 空列表（降级信号）。</summary>
    [Fact]
    public void ParseLine_PlainText_EmptySegments()
    {
        Assert.Empty(Parse("hello world"));
    }

    /// <summary>空行 — 空列表。</summary>
    [Fact]
    public void ParseLine_Empty_EmptySegments()
    {
        Assert.Empty(Parse(string.Empty));
    }

    /// <summary>粗体 — plain/bold/plain 三段顺序。</summary>
    [Fact]
    public void ParseLine_BoldDelimited_ThreeOrderedSegments()
    {
        var segments = Parse("a **b** c");

        Assert.Equal(3, segments.Count);
        Assert.Equal(new MarkdownSegment("a ", MarkdownStyle.None), segments[0]);
        Assert.Equal(new MarkdownSegment("b", MarkdownStyle.Bold), segments[1]);
        Assert.Equal(new MarkdownSegment(" c", MarkdownStyle.None), segments[2]);
    }

    /// <summary>斜体 — 单星定界中段 italic。</summary>
    [Fact]
    public void ParseLine_ItalicDelimited_ItalicSegment()
    {
        var segments = Parse("a *b* c");

        Assert.Equal(3, segments.Count);
        Assert.Equal(new MarkdownSegment("b", MarkdownStyle.Italic), segments[1]);
    }

    /// <summary>行内代码 — 反引号定界中段 code。</summary>
    [Fact]
    public void ParseLine_CodeDelimited_CodeSegment()
    {
        var segments = Parse("run `x` now");

        Assert.Equal(3, segments.Count);
        Assert.Equal(new MarkdownSegment("x", MarkdownStyle.Code), segments[1]);
    }

    /// <summary>三连星 — Bold|Italic 单段。</summary>
    [Fact]
    public void ParseLine_TripleDelimiters_BoldItalicSegment()
    {
        var segments = Parse("***x***");

        var segment = Assert.Single(segments);
        Assert.Equal(new MarkdownSegment("x", MarkdownStyle.Bold | MarkdownStyle.Italic), segment);
    }

    /// <summary>未闭合星号（数学乘号）— 空列表字面降级。</summary>
    [Fact]
    public void ParseLine_UnclosedAsterisk_EmptySegments()
    {
        Assert.Empty(Parse("2 * 3 = 6"));
    }

    /// <summary>未闭合反引号 — 空列表字面降级。</summary>
    [Fact]
    public void ParseLine_UnclosedBacktick_EmptySegments()
    {
        Assert.Empty(Parse("a ` b"));
    }

    /// <summary>下划线不支持斜体 — snake_case 误判防护。</summary>
    [Fact]
    public void ParseLine_Underscore_NotDelimited_EmptySegments()
    {
        Assert.Empty(Parse("_var_name_"));
    }

    /// <summary>多 code 段 — 顺序五段（plain 夹于其间）。</summary>
    [Fact]
    public void ParseLine_MultipleCodes_FiveOrderedSegments()
    {
        var segments = Parse("a `x` b `y` c");

        Assert.Equal(5, segments.Count);
        Assert.Equal(new MarkdownSegment("x", MarkdownStyle.Code), segments[1]);
        Assert.Equal(new MarkdownSegment(" b ", MarkdownStyle.None), segments[2]);
        Assert.Equal(new MarkdownSegment("y", MarkdownStyle.Code), segments[3]);
        Assert.Equal(new MarkdownSegment(" c", MarkdownStyle.None), segments[4]);
    }

    /// <summary>平级不递归 — 粗体内反引号字面保留（bold 段含 ` 字符）。</summary>
    [Fact]
    public void ParseLine_BoldWrappingCode_NoNesting_LiteralPreserved()
    {
        var segments = Parse("a **`x`** b");

        Assert.Equal(3, segments.Count);
        Assert.Equal(new MarkdownSegment("`x`", MarkdownStyle.Bold), segments[1]);
    }
}
