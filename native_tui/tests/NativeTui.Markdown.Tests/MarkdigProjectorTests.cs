namespace NativeTui.Markdown.Tests;

/// <summary>
/// Markdig 投影器测试（M7-4）— Markdig AST → Core 块模型：
/// 职责分离：投影器只产出结构+行内样式 flags（标题级别样式由布局层施加）；
/// 行内 bold/em/code/link 投影为 flags；删除线忽略（基准禁用 del 解析）；
/// html 块丢弃（基准 def/del/html 不渲染）；链接 mailto 降级纯文本；图片显示 URL。
/// </summary>
public sealed class MarkdigProjectorTests
{
    /// <summary>投影并取首个块。</summary>
    private static MarkdownBlock FirstBlock(string markdown)
    {
        var block = MarkdigProjector.Project(markdown).Blocks.FirstOrDefault();
        Assert.NotNull(block);
        return block;
    }

    /// <summary>取块内唯一段落的段列表。</summary>
    private static IReadOnlyList<MarkdownSegment> InlineOf(MarkdownBlock block) =>
        Assert.IsType<MarkdownParagraph>(block).Segments;

    [Fact]
    public void Project_HeadingKeepsLevelAndPlainSegments()
    {
        var heading = Assert.IsType<MarkdownHeading>(FirstBlock("# Title"));

        Assert.Equal(1, heading.Level);
        var segment = Assert.Single(heading.Segments);
        Assert.Equal(("Title", MarkdownStyle.None), (segment.Text, segment.Style));
    }

    [Fact]
    public void Project_ParagraphInlineStyles()
    {
        var segments = InlineOf(FirstBlock("plain **bold** *em* `code`"));

        Assert.Equal(
        [
            ("plain ", MarkdownStyle.None),
            ("bold", MarkdownStyle.Bold),
            (" ", MarkdownStyle.None),
            ("em", MarkdownStyle.Italic),
            (" ", MarkdownStyle.None),
            ("code", MarkdownStyle.Code),
        ], segments.Select(static segment => (segment.Text, segment.Style)));
    }

    [Fact]
    public void Project_NestedEmphasisCombinesFlags()
    {
        var segments = InlineOf(FirstBlock("***both***"));

        var segment = Assert.Single(segments);
        Assert.Equal(MarkdownStyle.Bold | MarkdownStyle.Italic, segment.Style);
    }

    [Fact]
    public void Project_FencedCodeKeepsLanguageAndText()
    {
        var code = Assert.IsType<MarkdownCode>(FirstBlock("```cs\nvar a = 1;\nvar b = 2;\n```"));

        Assert.Equal("cs", code.Language);
        Assert.Equal("var a = 1;\nvar b = 2;", code.Text.TrimEnd('\n'));
    }

    [Fact]
    public void Project_IndentedCodeHasEmptyLanguage()
    {
        var code = Assert.IsType<MarkdownCode>(FirstBlock("    indented code"));

        Assert.Equal(string.Empty, code.Language);
        Assert.Equal("indented code", code.Text.TrimEnd('\n'));
    }

    [Fact]
    public void Project_QuoteNestsChildBlocks()
    {
        var quote = Assert.IsType<MarkdownQuote>(FirstBlock("> quoted"));

        var paragraph = Assert.IsType<MarkdownParagraph>(Assert.Single(quote.Children));
        var segment = Assert.Single(paragraph.Segments);
        Assert.Equal("quoted", segment.Text);
    }

    [Fact]
    public void Project_UnorderedListItems()
    {
        var list = Assert.IsType<MarkdownList>(FirstBlock("- first\n- second"));

        Assert.False(list.Ordered);
        Assert.Equal(2, list.Items.Count);
        var first = Assert.IsType<MarkdownParagraph>(Assert.Single(list.Items[0].Children));
        Assert.Equal("first", Assert.Single(first.Segments).Text);
    }

    [Fact]
    public void Project_OrderedListKeepsStart()
    {
        var list = Assert.IsType<MarkdownList>(FirstBlock("3. a\n4. b"));

        Assert.True(list.Ordered);
        Assert.Equal(3, list.Start);
    }

    [Fact]
    public void Project_NestedListInsideItem()
    {
        var list = Assert.IsType<MarkdownList>(FirstBlock("- outer\n  - inner"));

        var item = list.Items[0];
        Assert.Equal(2, item.Children.Count);
        var nested = Assert.IsType<MarkdownList>(item.Children[1]);
        Assert.False(nested.Ordered);
        Assert.Single(nested.Items);
    }

    [Fact]
    public void Project_ThematicBreakAsRule()
    {
        Assert.IsType<MarkdownRule>(FirstBlock("---"));
    }

    [Fact]
    public void Project_PipeTableHeadersAndRows()
    {
        var table = Assert.IsType<MarkdownTable>(FirstBlock("| Name | Value |\n| --- | --- |\n| a | b |"));

        Assert.Equal(["Name", "Value"], table.Headers.Select(PlainText));
        var row = Assert.Single(table.Rows);
        Assert.Equal(["a", "b"], row.Select(PlainText));
    }

    [Fact]
    public void Project_LinkTextCarriesLinkFlag()
    {
        var segments = InlineOf(FirstBlock("[text](https://example.com)"));

        var segment = Assert.Single(segments);
        Assert.Equal("text", segment.Text);
        Assert.Equal(MarkdownStyle.Link, segment.Style);
    }

    [Fact]
    public void Project_MailtoLinkDegradesToPlainEmail()
    {
        var segments = InlineOf(FirstBlock("[mail me](mailto:a@b.c)"));

        var segment = Assert.Single(segments);
        Assert.Equal("mail me", segment.Text);
        Assert.Equal(MarkdownStyle.None, segment.Style);
    }

    [Fact]
    public void Project_ImageShowsUrl()
    {
        var segments = InlineOf(FirstBlock("![alt](https://example.com/i.png)"));

        var segment = Assert.Single(segments);
        Assert.Equal("https://example.com/i.png", segment.Text);
    }

    [Fact]
    public void Project_StrikethroughIgnoredPerBaseline()
    {
        // 基准 configureMarked 禁用 del 解析（~ 常用于"约等于"）→ 投影不加删除线
        var segments = InlineOf(FirstBlock("~~text~~"));

        var segment = Assert.Single(segments);
        Assert.Equal("text", segment.Text);
        Assert.Equal(MarkdownStyle.None, segment.Style);
    }

    [Fact]
    public void Project_HtmlBlockDropped()
    {
        var document = MarkdigProjector.Project("<div>raw</div>\n\nvisible");

        Assert.Single(document.Blocks);
        Assert.Equal("visible", Assert.Single(InlineOf(document.Blocks[0])).Text);
    }

    [Fact]
    public void Project_MultipleParagraphs()
    {
        var document = MarkdigProjector.Project("first\n\nsecond");

        Assert.Equal(2, document.Blocks.Count);
        Assert.Equal("first", Assert.Single(InlineOf(document.Blocks[0])).Text);
        Assert.Equal("second", Assert.Single(InlineOf(document.Blocks[1])).Text);
    }

    [Fact]
    public void Project_TableCellKeepsInlineStyles()
    {
        var table = Assert.IsType<MarkdownTable>(FirstBlock("| Name |\n| --- |\n| `code` |"));

        var cell = table.Rows[0][0];
        var segment = Assert.Single(cell);
        Assert.Equal(MarkdownStyle.Code, segment.Style);
    }

    /// <summary>拼接段列表纯文本。</summary>
    private static string PlainText(IReadOnlyList<MarkdownSegment> segments) =>
        string.Concat(segments.Select(static segment => segment.Text));
}
