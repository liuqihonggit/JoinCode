namespace NativeTui.Tests.Components;

/// <summary>
/// 边框绘制测试 — cli-boxes round 风格 + 标志性"只画顶边"输入框边框。
/// 期望字符与颜色严格对齐基准 render-border.ts / PromptInput.tsx:2268。
/// </summary>
public sealed class BorderPainterTests
{
    [Fact]
    public void DrawTopOnly_RoundCorners()
    {
        using var screen = new Screen(cols: 8, rows: 2);
        var style = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.PromptBorder });
        BorderPainter.DrawTopOnly(screen, new Rect(0, 0, 8, 2), style);
        var line = RowText(screen, 0);
        Assert.Equal("╭──────╮", line);
        // 底行无边框
        Assert.Equal("        ", RowText(screen, 1));
    }

    [Fact]
    public void DrawTopOnly_AppliedStyle()
    {
        using var screen = new Screen(cols: 8, rows: 2);
        var style = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.PromptBorder });
        BorderPainter.DrawTopOnly(screen, new Rect(0, 0, 8, 2), style);
        Assert.Equal(style, screen[0, 0].StyleId);
        Assert.Equal(style, screen[0, 7].StyleId);
    }

    [Fact]
    public void DrawTopOnly_OffsetRect()
    {
        using var screen = new Screen(cols: 12, rows: 3);
        var style = screen.Styles.GetOrAdd(new TextStyle { Foreground = Theme.Dark.PromptBorder });
        BorderPainter.DrawTopOnly(screen, new Rect(2, 1, 6, 2), style);
        Assert.Equal("╭────╮", Extract(screen, 1, 2, 6));
        Assert.Equal("  ", Extract(screen, 0, 0, 2));
    }

    [Fact]
    public void DrawFull_RoundBox()
    {
        using var screen = new Screen(cols: 6, rows: 3);
        var style = 0;
        BorderPainter.DrawFull(screen, new Rect(0, 0, 6, 3), style);
        Assert.Equal("╭────╮", RowText(screen, 0));
        Assert.Equal("│    │", RowText(screen, 1));
        Assert.Equal("╰────╯", RowText(screen, 2));
    }

    [Fact]
    public void DrawFull_TooSmall_NoDraw()
    {
        using var screen = new Screen(cols: 1, rows: 1);
        BorderPainter.DrawFull(screen, new Rect(0, 0, 1, 1), 0);
        Assert.Equal(' ', screen[0, 0].Value.Value);
    }

    private static string RowText(Screen screen, int row)
    {
        var builder = new StringBuilder();
        for (var col = 0; col < screen.Cols; col++)
        {
            builder.Append(screen[row, col].Value.ToString());
        }

        return builder.ToString();
    }

    private static string Extract(Screen screen, int row, int col, int width)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < width; i++)
        {
            builder.Append(screen[row, col + i].Value.ToString());
        }

        return builder.ToString();
    }
}

/// <summary>
/// 静态文本组件测试。
/// </summary>
public sealed class TextBlockTests
{
    [Fact]
    public void Render_WritesStyledText()
    {
        using var screen = new Screen(cols: 20, rows: 1);
        var text = new TextBlock("hello");
        text.Render(screen, new Rect(0, 0, 20, 1), renderStyle: null);
        Assert.Equal("hello", RowOf(screen, 0));
    }

    [Fact]
    public void Render_TruncatesToFrame()
    {
        using var screen = new Screen(cols: 3, rows: 1);
        var text = new TextBlock("hello");
        text.Render(screen, new Rect(0, 0, 3, 1), renderStyle: null);
        Assert.Equal("hel", RowOf(screen, 0));
    }

    [Fact]
    public void MeasureHeight_SingleLine()
    {
        var text = new TextBlock("hello");
        Assert.Equal(1, text.MeasureHeight(width: 10));
    }

    private static string RowOf(Screen screen, int row) =>
        screen.ToAnsiText().Split('\n')[row];
}
