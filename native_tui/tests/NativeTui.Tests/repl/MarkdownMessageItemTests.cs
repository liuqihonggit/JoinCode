namespace NativeTui.Tests.Repl;

/// <summary>
/// Markdown 消息项渲染测试（M7-4）— 对齐基准 AssistantTextMessage：
/// ● 圆点占 col0（NoSelect minWidth 2，text 色），Markdown 各行从 col2 起（flex row 布局续行对齐）；
/// 行内 code 段 = permission 色前景（基准 codespan color('permission')）；块级样式由布局层合入段 flags。
/// </summary>
public sealed class MarkdownMessageItemTests
{
    /// <summary>构造文本段。</summary>
    private static MarkdownSegment Seg(string text, MarkdownStyle style = MarkdownStyle.None) => new(text, style);

    [Fact]
    public void Render_DotInFirstColumnAndBodyIndentedTwoColumns()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new MarkdownMessageItem(new MarkdownDocument([new MarkdownParagraph([Seg("hello")])]));

        item.Render(screen, new Rect(0, 0, 40, 4), Theme.Dark);

        // row0 margin 留空；row1 = ● text 色（col0）+ col2 起正文（基准 NoSelect minWidth 2）
        var text = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Text });
        Assert.Equal(new Rune(' '), screen[0, 0].Value);
        Assert.Equal(new Rune('●'), screen[1, 0].Value);
        Assert.Equal(text, screen[1, 0].StyleId);
        Assert.Equal(new Rune(' '), screen[1, 1].Value);
        Assert.Equal(new Rune('h'), screen[1, 2].Value);
        Assert.Equal(0, screen[1, 2].StyleId);
        Assert.Equal("● hello", RowText.Of(screen, 1, 0, 7));
    }

    [Fact]
    public void Render_WrappedLinesAlignAtColumnTwo()
    {
        using var screen = new Screen(cols: 40, rows: 4);
        var item = new MarkdownMessageItem(new MarkdownDocument([new MarkdownParagraph([Seg("aaaa bbbb")])]));

        // 窄 frame（宽 8）触发按渲染宽度布局：内容可用宽 = 8 - 2 缩进 = 6
        item.Render(screen, new Rect(0, 0, 8, 4), Theme.Dark);

        Assert.Equal("● aaaa", RowText.Of(screen, 1, 0, 6));
        Assert.Equal("  bbbb", RowText.Of(screen, 2, 0, 6));
    }

    [Fact]
    public void Render_InlineCodeUsesPermissionColor()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        var item = new MarkdownMessageItem(new MarkdownDocument(
            [new MarkdownParagraph([Seg("run "), Seg("npm test", MarkdownStyle.Code)])]));

        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        // 基准 codespan = color('permission')：行内代码段前景 permission 色
        var code = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.Permission });
        Assert.Equal(new Rune('n'), screen[1, 6].Value);
        Assert.Equal(code, screen[1, 6].StyleId);
        Assert.Equal(code, screen[1, 7].StyleId);
        Assert.Equal(0, screen[1, 3].StyleId);
    }

    [Fact]
    public void Render_QuoteBarDimmed()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        var item = new MarkdownMessageItem(new MarkdownDocument(
            [new MarkdownQuote([new MarkdownParagraph([Seg("quoted")])])]));

        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal(new Rune('▎'), screen[1, 2].Value);
        Assert.Equal(dim, screen[1, 2].StyleId);
    }

    [Fact]
    public void Render_StrongSegmentBold()
    {
        using var screen = new Screen(cols: 40, rows: 3);
        var item = new MarkdownMessageItem(new MarkdownDocument(
            [new MarkdownParagraph([Seg("bold text", MarkdownStyle.Bold)])]));

        item.Render(screen, new Rect(0, 0, 40, 3), Theme.Dark);

        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        Assert.Equal(new Rune('b'), screen[1, 2].Value);
        Assert.Equal(bold, screen[1, 2].StyleId);
    }

    [Fact]
    public void Render_BlocksSeparatedByBlankRow()
    {
        using var screen = new Screen(cols: 40, rows: 6);
        var item = new MarkdownMessageItem(new MarkdownDocument(
        [
            new MarkdownParagraph([Seg("para")]),
            new MarkdownCode("txt", "code"),
        ]));

        item.Render(screen, new Rect(0, 0, 40, 6), Theme.Dark);

        // margin 1 + para + 空行 + code = 4 行；空行 row3 保持空白
        Assert.Equal("● para", RowText.Of(screen, 1, 0, 6));
        Assert.Equal("  code", RowText.Of(screen, 3, 0, 6));
        Assert.Equal(new Rune(' '), screen[2, 0].Value);
        Assert.Equal(new Rune(' '), screen[2, 2].Value);
    }

    [Fact]
    public void MeasureHeight_MarginPlusLayoutLineCount()
    {
        // width 8（内容宽 6）→ "aaaa bb"/"bb" 2 行 → margin 1 + 2 = 3
        var item = new MarkdownMessageItem(new MarkdownDocument([new MarkdownParagraph([Seg("aaaa bbbb")])]));
        Assert.Equal(3, item.MeasureHeight(width: 8));

        // 单行 → margin 1 + 1 = 2
        var single = new MarkdownMessageItem(new MarkdownDocument([new MarkdownParagraph([Seg("hi")])]));
        Assert.Equal(2, single.MeasureHeight(width: 80));
    }

    [Fact]
    public void MeasureHeight_WidthSensitiveLayoutCached()
    {
        var item = new MarkdownMessageItem(new MarkdownDocument([new MarkdownParagraph([Seg("aaaa bbbb")])]));

        Assert.Equal(2, item.MeasureHeight(width: 80));
        Assert.Equal(3, item.MeasureHeight(width: 8));
        Assert.Equal(2, item.MeasureHeight(width: 80));
    }
}
