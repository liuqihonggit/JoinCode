namespace NativeTui.Tests.Markdown;

/// <summary>
/// 段内硬换行门禁 — 真机现象：`&gt; 第一行\n&gt; 第二行` 渲染成 `▎ 引用块第一行引用块第二行`（两行粘连）。
/// 根因：软换行（\n）在 Tokenize 里当普通零宽字符并入同一 token，布局层从不换行，写屏时被跳过。
/// 门禁锁三件事：段内 \n 必须产生新物理行；每行都带容器前缀（引用 ▎）；\r\n 与尾换行不得留 \r 或空行。
/// </summary>
public sealed class MarkdownSoftBreakTests
{
    /// <summary>无样式文本段。</summary>
    private static MarkdownSegment Seg(string text) => new(text, MarkdownStyle.None);

    /// <summary>布局行 → 纯文本。</summary>
    private static string PlainText(IReadOnlyList<MarkdownSegment> line) =>
        string.Concat(line.Select(segment => segment.Text));

    [Fact]
    public void Paragraph_WithNewline_SplitsIntoTwoLines()
    {
        var doc = new MarkdownDocument([new MarkdownParagraph([Seg("第一行\n第二行")])]);

        var lines = MarkdownLayout.Layout(doc, width: 80).Select(PlainText).ToArray();

        Assert.Equal(["第一行", "第二行"], lines);
    }

    [Fact]
    public void Quote_WithNewline_EachLineKeepsPrefix()
    {
        var doc = new MarkdownDocument([new MarkdownQuote([new MarkdownParagraph([Seg("第一行\n第二行")])])]);

        var lines = MarkdownLayout.Layout(doc, width: 80).Select(PlainText).ToArray();

        Assert.Equal(["▎ 第一行", "▎ 第二行"], lines);
    }

    [Fact]
    public void Paragraph_CrLf_NoCarriageReturnLeaks()
    {
        var doc = new MarkdownDocument([new MarkdownParagraph([Seg("a\r\nb")])]);

        var lines = MarkdownLayout.Layout(doc, width: 80).Select(PlainText).ToArray();

        Assert.Equal(["a", "b"], lines);
        Assert.DoesNotContain('\r', string.Concat(lines));
    }

    [Fact]
    public void Paragraph_TrailingNewline_ProducesNoEmptyLine()
    {
        var doc = new MarkdownDocument([new MarkdownParagraph([Seg("a\n")])]);

        var lines = MarkdownLayout.Layout(doc, width: 80).Select(PlainText).ToArray();

        Assert.Equal(["a"], lines);
    }

    [Fact]
    public void Paragraph_LeadingNewline_ProducesNoLeadingEmptyLine()
    {
        var doc = new MarkdownDocument([new MarkdownParagraph([Seg("\n结果")])]);

        var lines = MarkdownLayout.Layout(doc, width: 80).Select(PlainText).ToArray();

        Assert.Equal(["结果"], lines);
    }

    [Fact]
    public void ListItem_WithNewline_ContinuationAlignedToContentColumn()
    {
        var doc = new MarkdownDocument([
            new MarkdownList(false, 1, [new MarkdownListItem([new MarkdownParagraph([Seg("第一行\n第二行")])])]),
        ]);

        var lines = MarkdownLayout.Layout(doc, width: 80).Select(PlainText).ToArray();

        Assert.Equal(["- 第一行", "  第二行"], lines);
    }
}
