namespace JoinCode.Gui.Tests.Markdown;

/// <summary>
/// MarkdownParser 单元测试 — 验证 Markdig AST → MarkdownRenderModel 的转换契约。
/// 覆盖标题/段落/粗体/斜体/行内代码/链接/代码块/列表/表格/引用/分隔线/删除线。
/// </summary>
public class MarkdownParserTests
{
    private static IReadOnlyList<MarkdownBlock> Parse(string markdown) => MarkdownParser.Parse(markdown);

    private static MarkdownParagraph SingleParagraph(string markdown)
    {
        var blocks = Parse(markdown);
        blocks.Should().HaveCount(1);
        return blocks[0].Should().BeOfType<MarkdownParagraph>().Subject;
    }

    [Fact]
    public void Parse_PlainText_ProducesSingleTextParagraph()
    {
        var p = SingleParagraph("hello world");

        p.Inlines.Where(i => i is MarkdownText { Text: "hello world" }).Should().ContainSingle();
    }

    [Fact]
    public void Parse_Heading_ProducesHeadingWithLevel()
    {
        var blocks = Parse("# Title");
        blocks.Should().ContainSingle();
        var h = blocks[0].Should().BeOfType<MarkdownHeading>().Subject;
        h.Level.Should().Be(1);
        h.Inlines.Where(i => i is MarkdownText { Text: "Title" }).Should().ContainSingle();
    }

    [Fact]
    public void Parse_HeadingLevelThree_ProducesLevel3()
    {
        var blocks = Parse("### Sub");
        blocks.Should().ContainSingle();
        blocks[0].Should().BeOfType<MarkdownHeading>().Subject.Level.Should().Be(3);
    }

    [Fact]
    public void Parse_Bold_ProducesMarkdownBoldWithText()
    {
        var p = SingleParagraph("a **bold** b");
        p.Inlines.Where(i => i is MarkdownBold { Children: [{ }] }).Should().ContainSingle();
        var bold = p.Inlines.OfType<MarkdownBold>().Single();
        bold.Children.Where(c => c is MarkdownText { Text: "bold" }).Should().ContainSingle();
    }

    [Fact]
    public void Parse_Italic_ProducesMarkdownItalicWithText()
    {
        var p = SingleParagraph("a *italic* b");
        var italic = p.Inlines.OfType<MarkdownItalic>().Single();
        italic.Children.Where(c => c is MarkdownText { Text: "italic" }).Should().ContainSingle();
    }

    [Fact]
    public void Parse_InlineCode_ProducesMarkdownCode()
    {
        var p = SingleParagraph("use `code` here");
        p.Inlines.Where(i => i is MarkdownCode { Code: "code" }).Should().ContainSingle();
    }

    [Fact]
    public void Parse_Link_ProducesMarkdownLinkWithUrl()
    {
        var p = SingleParagraph("[docs](https://x.com)");
        var link = p.Inlines.OfType<MarkdownLink>().Single();
        link.Url.Should().Be("https://x.com");
        link.Children.Where(c => c is MarkdownText { Text: "docs" }).Should().ContainSingle();
    }

    [Fact]
    public void Parse_Strikethrough_ProducesMarkdownStrikethrough()
    {
        var p = SingleParagraph("~~gone~~");
        var s = p.Inlines.OfType<MarkdownStrikethrough>().Single();
        s.Children.Where(c => c is MarkdownText { Text: "gone" }).Should().ContainSingle();
    }

    [Fact]
    public void Parse_FencedCodeBlock_ProducesCodeBlockWithLanguage()
    {
        var blocks = Parse("```csharp\nint x = 1;\n```");
        blocks.Should().ContainSingle();
        var code = blocks[0].Should().BeOfType<MarkdownCodeBlock>().Subject;
        code.Language.Should().Be("csharp");
        code.Code.Should().Contain("int x = 1;");
    }

    [Fact]
    public void Parse_UnorderedList_ProducesMarkdownListItems()
    {
        var blocks = Parse("- alpha\n- beta");
        blocks.Should().ContainSingle();
        var list = blocks[0].Should().BeOfType<MarkdownList>().Subject;
        list.Ordered.Should().BeFalse();
        list.Items.Should().HaveCount(2);
        list.Items[0].Where(i => i is MarkdownText { Text: "alpha" }).Should().ContainSingle();
        list.Items[1].Where(i => i is MarkdownText { Text: "beta" }).Should().ContainSingle();
    }

    [Fact]
    public void Parse_OrderedList_ProducesOrderedMarkdownList()
    {
        var blocks = Parse("1. first\n2. second");
        var list = blocks[0].Should().BeOfType<MarkdownList>().Subject;
        list.Ordered.Should().BeTrue();
        list.Items.Should().HaveCount(2);
    }

    [Fact]
    public void Parse_Table_ProducesHeaderAndRows()
    {
        var blocks = Parse("| a | b |\n|---|---|\n| 1 | 2 |");
        blocks.Should().ContainSingle();
        var table = blocks[0].Should().BeOfType<MarkdownTable>().Subject;
        table.Header.Should().BeEquivalentTo(["a", "b"]);
        table.Rows.Should().HaveCount(1);
        table.Rows[0].Should().BeEquivalentTo(["1", "2"]);
    }

    [Fact]
    public void Parse_Quote_ProducesMarkdownQuote()
    {
        var blocks = Parse("> quoted text");
        blocks.Should().ContainSingle();
        var quote = blocks[0].Should().BeOfType<MarkdownQuote>().Subject;
        quote.Inlines.Where(i => i is MarkdownText { Text: "quoted text" }).Should().ContainSingle();
    }

    [Fact]
    public void Parse_ThematicBreak_ProducesMarkdownRule()
    {
        var blocks = Parse("---");
        blocks.Should().ContainSingle();
        blocks[0].Should().BeOfType<MarkdownRule>();
    }

    [Fact]
    public void Parse_MixedDocument_ProducesMultipleBlocksInOrder()
    {
        var md = "# H\n\nparagraph\n\n```\ncode\n```";
        var blocks = Parse(md);
        blocks.Should().HaveCount(3);
        blocks[0].Should().BeOfType<MarkdownHeading>();
        blocks[1].Should().BeOfType<MarkdownParagraph>();
        blocks[2].Should().BeOfType<MarkdownCodeBlock>();
    }
}
