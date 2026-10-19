namespace NativeTui.Tests.ScreenTests;

/// <summary>
/// Rune 宽度测试 — East Asian Width 简化表（对齐基准 get-east-asian-width 依赖的核心区间）。
/// Ambiguous 字符（⏺ ✻ ● 等）按非 CJK 上下文宽度 1 处理 — 基准 figures.ts 的 darwin/win 差异正是 Ambiguous。
/// </summary>
public sealed class RuneWidthTests
{
    [Theory]
    [InlineData('a', 1)]
    [InlineData('Z', 1)]
    [InlineData('❯', 1)]
    [InlineData('⏺', 1)]
    [InlineData('●', 1)]
    [InlineData('✻', 1)]
    [InlineData('⎿', 1)]
    [InlineData('∙', 1)]
    public void AmbiguousAndAscii_Width1(char ch, int expected)
    {
        Assert.Equal(expected, RuneWidth.Of(new Rune(ch)));
    }

    [Theory]
    [InlineData('中', 2)]
    [InlineData('文', 2)]
    [InlineData('！', 2)]   // U+FF01 全角
    [InlineData('）', 2)]   // U+FF09
    public void CjkAndFullwidth_Width2(char ch, int expected)
    {
        Assert.Equal(expected, RuneWidth.Of(new Rune(ch)));
    }

    [Fact]
    public void EmojiSupplementaryPlane_Width2()
    {
        // U+1F600 😀
        Assert.Equal(2, RuneWidth.Of(new Rune(0x1F600)));
    }

    [Fact]
    public void ZeroWidthJoiner_Width0()
    {
        Assert.Equal(0, RuneWidth.Of(new Rune(0x200D)));
    }
}

/// <summary>
/// Screen cell 网格测试 — 写入/宽字符占位/越界截断/Resize/golden 序列化。
/// </summary>
public sealed class ScreenTests
{
    [Fact]
    public void NewScreen_AllBlankDefaultStyle()
    {
        using var screen = new Screen(cols: 10, rows: 3);
        Assert.Equal(10, screen.Cols);
        Assert.Equal(3, screen.Rows);
        for (var row = 0; row < 3; row++)
        {
            for (var col = 0; col < 10; col++)
            {
                Assert.Equal(' ', screen[row, col].Value.Value);
                Assert.Equal(0, screen[row, col].StyleId);
                Assert.Equal(CellWidth.Default, screen[row, col].Width);
            }
        }
    }

    [Fact]
    public void WriteText_Ascii_PlacedSequentially()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var end = screen.WriteText(0, 0, "hello");
        Assert.Equal(5, end);
        Assert.Equal('h', screen[0, 0].Value.Value);
        Assert.Equal('o', screen[0, 4].Value.Value);
        Assert.Equal(' ', screen[0, 5].Value.Value);
    }

    [Fact]
    public void WriteText_StyleIdApplied()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        screen.WriteText(0, 0, "ab", bold);
        Assert.Equal(bold, screen[0, 0].StyleId);
        Assert.Equal(bold, screen[0, 1].StyleId);
    }

    [Fact]
    public void WriteText_Cjk_OccupiesWidePlusSpacerTail()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var end = screen.WriteText(0, 0, "中文");
        Assert.Equal(4, end);
        Assert.Equal('中', screen[0, 0].Value.Value);
        Assert.Equal(CellWidth.Wide, screen[0, 0].Width);
        Assert.Equal(CellWidth.SpacerTail, screen[0, 1].Width);
        Assert.Equal('文', screen[0, 2].Value.Value);
        Assert.Equal(CellWidth.SpacerTail, screen[0, 3].Width);
    }

    [Fact]
    public void WriteText_MixedAsciiAndCjk()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var end = screen.WriteText(0, 0, "a中b");
        Assert.Equal(4, end);
        Assert.Equal('a', screen[0, 0].Value.Value);
        Assert.Equal(CellWidth.Wide, screen[0, 1].Width);
        Assert.Equal(CellWidth.SpacerTail, screen[0, 2].Width);
        Assert.Equal('b', screen[0, 3].Value.Value);
    }

    [Fact]
    public void WriteText_WideCharAtLastColumn_Truncated()
    {
        using var screen = new Screen(cols: 3, rows: 1);
        var end = screen.WriteText(0, 2, "中");
        // 最后一列放不下宽字符 → 停止
        Assert.Equal(2, end);
        Assert.Equal(' ', screen[0, 2].Value.Value);
    }

    [Fact]
    public void WriteText_PastRowEnd_StopsAtBoundary()
    {
        using var screen = new Screen(cols: 3, rows: 1);
        var end = screen.WriteText(0, 1, "abcd");
        Assert.Equal(3, end);
        Assert.Equal('a', screen[0, 1].Value.Value);
        Assert.Equal('b', screen[0, 2].Value.Value);
    }

    [Fact]
    public void WriteText_EmojiSupplementaryPlane_Wide()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var end = screen.WriteText(0, 0, "😀");
        Assert.Equal(2, end);
        Assert.Equal(CellWidth.Wide, screen[0, 0].Width);
        Assert.Equal(0x1F600, screen[0, 0].Value.Value);
    }

    [Fact]
    public void Clear_ResetsAllCells()
    {
        using var screen = new Screen(cols: 5, rows: 2);
        screen.WriteText(0, 0, "hello");
        screen.Clear();
        Assert.Equal(' ', screen[0, 0].Value.Value);
    }

    [Fact]
    public void Resize_PreservesTopLeft()
    {
        using var screen = new Screen(cols: 10, rows: 5);
        screen.WriteText(0, 0, "keep");
        screen.WriteText(4, 0, "cut");
        screen.Resize(cols: 6, rows: 2);
        Assert.Equal(6, screen.Cols);
        Assert.Equal(2, screen.Rows);
        Assert.Equal('k', screen[0, 0].Value.Value);
        Assert.Equal('p', screen[0, 3].Value.Value);
        Assert.Equal(' ', screen[1, 0].Value.Value);
    }

    [Fact]
    public void ToAnsiText_PlainAscii_NoEscape()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        screen.WriteText(0, 0, "hi");
        Assert.Equal("hi", screen.ToAnsiText());
    }

    [Fact]
    public void ToAnsiText_StyledSegment_EmitsTransitionAndResetsAtEol()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var orange = screen.Styles.GetOrAdd(new TextStyle { Foreground = Color.FromRgb(215, 119, 87) });
        screen.WriteText(0, 0, "ab", orange);
        // 行尾停在非默认样式 → 必须重置（否则终端状态泄漏到下一行）
        Assert.Equal("\x1b[38;2;215;119;87mab\x1b[39m", screen.ToAnsiText());
    }

    [Fact]
    public void ToAnsiText_StyleChangeMidLine_EmitsBothTransitions()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        screen.WriteText(0, 0, "a", bold);
        screen.WriteText(0, 1, "b");
        Assert.Equal("\x1b[1ma\x1b[22mb", screen.ToAnsiText());
    }

    [Fact]
    public void ToAnsiText_CjkSpacerTailNotDuplicated()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        screen.WriteText(0, 0, "中");
        // 宽字符只输出 rune 一次，SpacerTail 不重复
        Assert.Equal("中", screen.ToAnsiText());
    }

    [Fact]
    public void ToAnsiText_TrailingStyleResetToDefaultNotEmitted()
    {
        using var screen = new Screen(cols: 10, rows: 1);
        var bold = screen.Styles.GetOrAdd(new TextStyle { Bold = true });
        screen.WriteText(0, 0, "a", bold);
        screen.WriteText(0, 1, "b");
        // 行尾回到 Default 不需要发序列
        Assert.Equal("\x1b[1ma\x1b[22mb", screen.ToAnsiText());
    }
}
