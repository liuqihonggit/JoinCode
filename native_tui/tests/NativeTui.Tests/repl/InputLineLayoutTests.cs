namespace NativeTui.Tests.Repl;

/// <summary>
/// 输入区软折行投影测试（DSG044 L3 — 基准 useTextInput Cursor.fromText 折行模型）：
/// 词边界贪心（空格留上行尾）+ grapheme 宽度（CJK/emoji 整块）+ 硬切 + 逻辑行边界 + 零折行降级。
/// </summary>
public sealed class InputLineLayoutTests
{
    /// <summary>短文本零折行 — 视觉行数 = 逻辑行数（单行降级兼容）。</summary>
    [Fact]
    public void ShortText_NoWrapping()
    {
        var lines = InputLineLayout.Wrap("hello", 80);
        var line = Assert.Single(lines);
        Assert.Equal(0, line.LogicalLine);
        Assert.Equal("hello", Segment("hello", line));
    }

    /// <summary>词边界贪心：宽度满后断在最近空格处（空格留上行尾）。</summary>
    [Fact]
    public void WordBoundary_WrapsAtNearestSpace()
    {
        var lines = InputLineLayout.Wrap("aaaa bbbb cccc", 9);
        Assert.Equal(2, lines.Count);
        Assert.Equal("aaaa ", Segment("aaaa bbbb cccc", lines[0]));
        Assert.Equal("bbbb cccc", Segment("aaaa bbbb cccc", lines[1]));
    }

    /// <summary>无断点硬切（CJK 逐字）：宽 4 列折 3 视觉行。</summary>
    [Fact]
    public void CjkText_HardWrapByWidth()
    {
        var text = "中文中文中文";
        var lines = InputLineLayout.Wrap(text, 4);
        Assert.Equal(3, lines.Count);
        Assert.All(lines, line => Assert.Equal("中文", Segment(text, line)));
    }

    /// <summary>逻辑行边界：\n 拆行各自独立折行。</summary>
    [Fact]
    public void LogicalLineBoundary_IndependentWrap()
    {
        var lines = InputLineLayout.Wrap("ab\ncccc dddd", 4);
        Assert.Equal(3, lines.Count);
        Assert.Equal(0, lines[0].LogicalLine);
        Assert.Equal(1, lines[1].LogicalLine);
        Assert.Equal(1, lines[2].LogicalLine);
        Assert.Equal("ab", Segment("ab\ncccc dddd", lines[0]));
    }

    /// <summary>grapheme 宽度精确：ZWJ emoji 家庭组合整块 2 列不拆半。</summary>
    [Fact]
    public void ZwjEmoji_KeptWhole()
    {
        var family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
        var lines = InputLineLayout.Wrap(family + "x", 2);
        Assert.Equal(2, lines.Count);
        Assert.Equal(family, Segment(family + "x", lines[0]));
        Assert.Equal("x", Segment(family + "x", lines[1]));
    }

    /// <summary>空文本/空行：单视觉行（空段）。</summary>
    [Fact]
    public void EmptyText_SingleEmptyVisualLine()
    {
        var lines = InputLineLayout.Wrap(string.Empty, 80);
        var line = Assert.Single(lines);
        Assert.Equal(0, line.Length);
    }

    /// <summary>超长无空格 token 硬切：URL 等长 token 按列切。</summary>
    [Fact]
    public void LongToken_HardWrap()
    {
        var lines = InputLineLayout.Wrap("aaaaaaaaaa", 4);
        Assert.Equal(3, lines.Count);
        Assert.Equal(4, lines[0].Length);
        Assert.Equal(4, lines[1].Length);
        Assert.Equal(2, lines[2].Length);
    }

    /// <summary>视觉行显示宽度：CJK 混排 grapheme 整块计宽。</summary>
    [Fact]
    public void DisplayWidth_GraphemeAware()
    {
        Assert.Equal(4, InputLineLayout.DisplayWidth("中文"));
        Assert.Equal(2, InputLineLayout.DisplayWidth("\U0001F468\u200D\U0001F469\u200D\U0001F467"));
        Assert.Equal(1, InputLineLayout.DisplayWidth("e\u0301"));
    }

    private static string Segment(string text, VisualLine line)
    {
        var lineText = PromptInputBox.SplitLogicalLines(text)[line.LogicalLine];
        return lineText.Substring(line.StartOffset, line.Length);
    }
}
