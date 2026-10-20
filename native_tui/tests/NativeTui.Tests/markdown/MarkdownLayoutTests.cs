namespace NativeTui.Tests.Markdown;

/// <summary>
/// Markdown 布局器测试（M7-4）— 块模型 → 物理行投影，对齐基准 formatToken 映射：
/// heading h1=bold+italic+underline、h2+=bold；blockquote 每行 ▎ dim 前缀 + 内容 italic
///（修复青蛙TUI 前缀只加一次的坑）；列表 2 空格/层缩进 + "- "/"N. " 前缀（有序分级编号）；
/// code 原文逐行；hr = "---"；表格 "| … |" + "---" 分隔行；块间空行 = 基准 Box gap=1。
/// </summary>
public sealed class MarkdownLayoutTests
{
    /// <summary>构造文本段。</summary>
    private static MarkdownSegment Seg(string text, MarkdownStyle style = MarkdownStyle.None) => new(text, style);

    /// <summary>构造表格单元格（段列表）。</summary>
    private static IReadOnlyList<MarkdownSegment> Cell(params MarkdownSegment[] segments) => segments;

    [Fact]
    public void Layout_ParagraphWrapsToWidth()
    {
        var doc = new MarkdownDocument([new MarkdownParagraph([Seg("aaaa bbbb cccc")])]);

        var lines = MarkdownLayout.Layout(doc, width: 10);

        Assert.Equal(["aaaa bbbb", "cccc"], lines.Select(PlainText));
    }

    [Fact]
    public void Layout_ParagraphPreservesSegmentStyles()
    {
        var doc = new MarkdownDocument([new MarkdownParagraph([Seg("plain "), Seg("code", MarkdownStyle.Code)])]);

        var lines = MarkdownLayout.Layout(doc, width: 80);

        var line = Assert.Single(lines);
        Assert.Equal(2, line.Count);
        Assert.Equal(("plain ", MarkdownStyle.None), (line[0].Text, line[0].Style));
        Assert.Equal(("code", MarkdownStyle.Code), (line[1].Text, line[1].Style));
    }

    [Fact]
    public void Layout_Heading1AddsBoldItalicUnderline()
    {
        var doc = new MarkdownDocument([new MarkdownHeading(1, [Seg("Title")])]);

        var lines = MarkdownLayout.Layout(doc, width: 80);

        var line = Assert.Single(lines);
        Assert.Equal(MarkdownStyle.Bold | MarkdownStyle.Italic | MarkdownStyle.Underline, line[0].Style);
    }

    [Fact]
    public void Layout_Heading2AndBeyondAddsBoldOnly()
    {
        foreach (var level in new[] { 2, 3, 6 })
        {
            var doc = new MarkdownDocument([new MarkdownHeading(level, [Seg("Sub")])]);

            var line = Assert.Single(MarkdownLayout.Layout(doc, width: 80));

            Assert.Equal(MarkdownStyle.Bold, line[0].Style);
        }
    }

    [Fact]
    public void Layout_QuotePrefixesEveryLineWithDimBarAndItalicBody()
    {
        // 引用含 2 行内容（换行由窄宽度触发）→ 每行都要有 ▎ 前缀（修复青蛙TUI 坑2：前缀只加一次）
        var doc = new MarkdownDocument([new MarkdownQuote([new MarkdownParagraph([Seg("aaaa bbbb cccc")])])]);

        var lines = MarkdownLayout.Layout(doc, width: 8);

        Assert.Equal(3, lines.Count);
        foreach (var line in lines)
        {
            Assert.Equal("▎ ", line[0].Text);
            Assert.Equal(MarkdownStyle.Dim, line[0].Style);
        }

        // 内容宽 = 8 - 2（▎ 前缀）= 6 → "aaaa"/"bbbb"/"cccc"；内容段叠 Italic
        Assert.Equal(("aaaa", MarkdownStyle.Italic), (lines[0][1].Text, lines[0][1].Style));
        Assert.Equal("bbbb", lines[1][1].Text);
    }

    [Fact]
    public void Layout_UnorderedListUsesDashPrefix()
    {
        var doc = new MarkdownDocument([new MarkdownList(false, 0,
        [
            new MarkdownListItem([new MarkdownParagraph([Seg("first")])]),
            new MarkdownListItem([new MarkdownParagraph([Seg("second")])]),
        ])]);

        var lines = MarkdownLayout.Layout(doc, width: 80);

        Assert.Equal(["- first", "- second"], lines.Select(PlainText));
    }

    [Fact]
    public void Layout_OrderedListNumbersFromStart()
    {
        var doc = new MarkdownDocument([new MarkdownList(true, 3,
        [
            new MarkdownListItem([new MarkdownParagraph([Seg("a")])]),
            new MarkdownListItem([new MarkdownParagraph([Seg("b")])]),
        ])]);

        var lines = MarkdownLayout.Layout(doc, width: 80);

        Assert.Equal(["3. a", "4. b"], lines.Select(PlainText));
    }

    [Fact]
    public void Layout_NestedListIndentsTwoSpacesPerDepth()
    {
        var doc = new MarkdownDocument([new MarkdownList(false, 0,
        [
            new MarkdownListItem(
            [
                new MarkdownParagraph([Seg("outer")]),
                new MarkdownList(false, 0, [new MarkdownListItem([new MarkdownParagraph([Seg("inner")])])]),
            ]),
        ])]);

        var lines = MarkdownLayout.Layout(doc, width: 80);

        Assert.Equal(["- outer", "  - inner"], lines.Select(PlainText));
    }

    [Fact]
    public void Layout_ListItemWrappedBodyAlignsWithPrefix()
    {
        var doc = new MarkdownDocument([new MarkdownList(false, 0,
        [
            new MarkdownListItem([new MarkdownParagraph([Seg("aaaa bbbb cccc")])]),
        ])]);

        var lines = MarkdownLayout.Layout(doc, width: 10);

        Assert.Equal(["- aaaa", "  bbbb", "  cccc"], lines.Select(PlainText));
    }

    [Fact]
    public void Layout_CodeRendersLinesVerbatim()
    {
        var doc = new MarkdownDocument([new MarkdownCode("cs", "var a = 1;\nvar b = 2;")]);

        var lines = MarkdownLayout.Layout(doc, width: 80);

        Assert.Equal(["var a = 1;", "var b = 2;"], lines.Select(PlainText));
    }

    [Fact]
    public void Layout_RuleRendersTripleDash()
    {
        var doc = new MarkdownDocument([new MarkdownRule()]);

        var line = Assert.Single(MarkdownLayout.Layout(doc, width: 80));

        Assert.Equal("---", PlainText(line));
    }

    [Fact]
    public void Layout_TableRendersPipeRowsWithSeparator()
    {
        // 1 行数据（a | bbbbbbb）；列宽 = max(显示宽, 3)：col0 = max("Name"=4, "a"=1) = 4；col1 = max("Value"=5, "bbbbbbb"=7) = 7
        var doc = new MarkdownDocument([new MarkdownTable(
            [Cell(Seg("Name")), Cell(Seg("Value"))],
            [[Cell(Seg("a")), Cell(Seg("bbbbbbb"))]])]);

        var lines = MarkdownLayout.Layout(doc, width: 80);

        Assert.Equal(
        [
            "| Name | Value   |",
            "|------|---------|",
            "| a    | bbbbbbb |",
        ], lines.Select(PlainText));
    }

    [Fact]
    public void Layout_BlocksSeparatedByBlankLine()
    {
        var doc = new MarkdownDocument(
        [
            new MarkdownParagraph([Seg("para")]),
            new MarkdownCode("txt", "code"),
        ]);

        var lines = MarkdownLayout.Layout(doc, width: 80);

        Assert.Equal(["para", "", "code"], lines.Select(PlainText));
    }

    [Fact]
    public void Layout_CjkTextWrapsPerRune()
    {
        var doc = new MarkdownDocument([new MarkdownParagraph([Seg("中文字符测试")])]);

        var lines = MarkdownLayout.Layout(doc, width: 4);

        Assert.Equal(["中文", "字符", "测试"], lines.Select(PlainText));
    }

    /// <summary>拼接一行全部段的纯文本。</summary>
    private static string PlainText(IReadOnlyList<MarkdownSegment> line) =>
        string.Concat(line.Select(static segment => segment.Text));
}
