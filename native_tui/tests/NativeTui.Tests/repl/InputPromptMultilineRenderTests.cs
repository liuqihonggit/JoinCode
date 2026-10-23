namespace NativeTui.Tests.Repl;

/// <summary>
/// 输入框多行渲染测试 — 对齐基准 PromptInput.tsx multiline：
/// 首行带 ❯ 前缀、续行顶格无前缀、底线随 frame 高度下移、光标跨行定位、
/// 行数超 frame 高度贴底滚动（光标行恒可见）、ghost 挂末行尾；
/// 单行降级回归：无换行时渲染输出与单行版逐字节一致（既有 WideCharRenderTests 行为零破坏）。
/// </summary>
public sealed class InputPromptMultilineRenderTests
{
    private const int FrameX = 0;
    private const int FrameY = 5;

    [Fact]
    public void SingleLine_RenderMatchesLegacyLayout()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var box = new PromptInputBox();

        box.Render(screen, new Rect(FrameX, FrameY, 40, 3), Theme.Dark, "abc", cursorCol: 2);

        // 首行 ❯ 前缀 + 文本；底线在 frame.Y+2（3 高 frame）
        Assert.Equal(new Rune('❯'), screen[FrameY + 1, FrameX].Value);
        Assert.Equal(new Rune('a'), screen[FrameY + 1, PromptInputBox.PromptWidth].Value);
        Assert.Equal(new Rune('c'), screen[FrameY + 1, PromptInputBox.PromptWidth + 2].Value);
        Assert.Equal(new Rune('─'), screen[FrameY + 2, FrameX].Value);
        // 光标反色块在文本 offset 2 处（PromptWidth+2）
        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        Assert.Equal(inverse, screen[FrameY + 1, PromptInputBox.PromptWidth + 2].StyleId);
    }

    [Fact]
    public void SingleLine_BottomRuleAtFrameBottom_EvenForTallerFrame()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var box = new PromptInputBox();

        // 单行文本 + 5 高 frame：文本仍在 row 1，底线在 frame 末行（frame.Y + Height - 1）
        box.Render(screen, new Rect(FrameX, FrameY, 40, 5), Theme.Dark, "abc", cursorCol: 0);

        Assert.Equal(new Rune('a'), screen[FrameY + 1, PromptInputBox.PromptWidth].Value);
        Assert.Equal(new Rune('─'), screen[FrameY + 4, FrameX].Value);
    }

    [Fact]
    public void Multiline_FirstLineHasPrompt_ContinuationLinesFlushWithoutPrefix()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var box = new PromptInputBox();

        box.Render(screen, new Rect(FrameX, FrameY, 40, 5), Theme.Dark, "aaa\nbbb\nccc", cursorCol: 0);

        // 首行：❯ + aaa（PromptWidth 起）
        Assert.Equal(new Rune('❯'), screen[FrameY + 1, FrameX].Value);
        Assert.Equal(new Rune('a'), screen[FrameY + 1, PromptInputBox.PromptWidth].Value);
        // 续行顶格无前缀（frame.X 起）
        Assert.Equal(new Rune('b'), screen[FrameY + 2, FrameX].Value);
        Assert.Equal(new Rune('c'), screen[FrameY + 3, FrameX].Value);
        // 底线在 frame 末行（5 高 → frame.Y+4）
        Assert.Equal(new Rune('─'), screen[FrameY + 4, FrameX].Value);
    }

    [Fact]
    public void Multiline_CursorPositionsAcrossLines()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var box = new PromptInputBox();

        // 光标在行 1 偏移 2（"aaa\nbb|b\nccc" 全文偏移 6）
        box.Render(screen, new Rect(FrameX, FrameY, 40, 5), Theme.Dark, "aaa\nbbb\nccc", cursorCol: 6);

        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        Assert.Equal(inverse, screen[FrameY + 2, FrameX + 2].StyleId);
    }

    [Fact]
    public void Multiline_CjkCursorUsesDisplayWidth()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var box = new PromptInputBox();

        // 行 1 中文 "你好"，光标在行 1 末尾（全文偏移 4 = "a\n你好"）
        box.Render(screen, new Rect(FrameX, FrameY, 40, 4), Theme.Dark, "a\n你好", cursorCol: 4);

        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        Assert.Equal(inverse, screen[FrameY + 2, FrameX + 4].StyleId);
    }

    [Fact]
    public void Multiline_TooManyLines_ScrollsToBottomKeepingCursorVisible()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var box = new PromptInputBox();

        // 4 行文本，frame 3 高（可见 1 行），光标在末行尾（"l4" 后，全文偏移 11）
        box.Render(screen, new Rect(FrameX, FrameY, 40, 3), Theme.Dark, "l1\nl2\nl3\nl4", cursorCol: 11);

        // 续行顶格（无前缀）— "l4" 占 FrameX/FrameX+1，光标 inverse 在 FrameX+2
        Assert.Equal(new Rune('l'), screen[FrameY + 1, FrameX].Value);
        Assert.Equal(new Rune('4'), screen[FrameY + 1, FrameX + 1].Value);
        Assert.Equal(new Rune('─'), screen[FrameY + 2, FrameX].Value);
        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        Assert.Equal(inverse, screen[FrameY + 1, FrameX + 2].StyleId);
    }

    [Fact]
    public void Multiline_CursorAboveWindow_SlidesWindowUp()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var box = new PromptInputBox();

        // 4 行文本，frame 3 高（可见 1 行），光标在首行 → 窗口滑到首行
        box.Render(screen, new Rect(FrameX, FrameY, 40, 3), Theme.Dark, "l1\nl2\nl3\nl4", cursorCol: 0);

        Assert.Equal(new Rune('l'), screen[FrameY + 1, PromptInputBox.PromptWidth].Value);
        Assert.Equal(new Rune('❯'), screen[FrameY + 1, FrameX].Value);
    }

    [Fact]
    public void Multiline_GhostSuffixRenderedAfterLastLine()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var box = new PromptInputBox();

        box.Render(screen, new Rect(FrameX, FrameY, 40, 5), Theme.Dark, "aaa\nbbb", cursorCol: 0, ghostSuffix: " ghost");

        // ghost 在末行（行 1）文本之后：frame.X + "bbb" 宽 3
        Assert.Equal(new Rune(' '), screen[FrameY + 2, FrameX + 3].Value);
        Assert.Equal(new Rune('g'), screen[FrameY + 2, FrameX + 4].Value);
    }

    [Fact]
    public void SingleLine_GhostSuffixStillAfterText()
    {
        using var screen = new Screen(cols: 40, rows: 12);
        var box = new PromptInputBox();

        box.Render(screen, new Rect(FrameX, FrameY, 40, 3), Theme.Dark, "ab", cursorCol: 0, ghostSuffix: "X");

        Assert.Equal(new Rune('X'), screen[FrameY + 1, PromptInputBox.PromptWidth + 2].Value);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 4)]
    [InlineData(5, 7)]
    public void HeightFor_LineCountPlusRules(int lineCount, int expectedHeight)
    {
        Assert.Equal(expectedHeight, PromptInputBox.HeightFor(lineCount));
    }

    [Theory]
    [InlineData("abc", 1)]
    [InlineData("a\nb", 2)]
    [InlineData("a\nb\nc", 3)]
    [InlineData("", 1)]
    [InlineData("\n", 2)]
    public void SplitLogicalLines_SplitsOnNewline(string text, int expectedCount)
    {
        Assert.Equal(expectedCount, PromptInputBox.SplitLogicalLines(text).Count);
    }

    [Theory]
    [InlineData(5, 3, 4, 2)]
    [InlineData(5, 3, 0, 0)]
    [InlineData(5, 3, 1, 1)]
    [InlineData(4, 4, 3, 0)]
    [InlineData(1, 3, 0, 0)]
    public void FirstVisibleLine_BottomAnchoredWithCursorPriority(int lineCount, int maxVisible, int cursorLine, int expected)
    {
        Assert.Equal(expected, PromptInputBox.FirstVisibleLine(lineCount, maxVisible, cursorLine));
    }

    /// <summary>软折行渲染（DSG044 L3 — 基准 Cursor.fromText 软折行模型）：单逻辑行超宽折为多视觉行，
    /// 首视觉行带 ❯ 前缀、视觉行续行顶格（旧行为右界截断尾部不可见 — 全部可见）。</summary>
    [Fact]
    public void SoftWrap_LongLine_RendersAllVisualLines()
    {
        using var screen = new Screen(cols: 20, rows: 10);
        var box = new PromptInputBox();
        var text = new string('a', 30) + new string('b', 10);

        // 折行宽度 = frame.Width - PromptWidth = 18 → 40 字符折 3 视觉行 → 高度 5
        box.Render(screen, new Rect(0, 0, 20, 5), Theme.Dark, text, cursorCol: 0);

        Assert.Equal(new Rune('❯'), screen[1, 0].Value);
        Assert.Equal(new Rune('a'), screen[1, PromptInputBox.PromptWidth].Value);
        // 第 2 视觉行顶格（第 18 个 a 起）
        Assert.Equal(new Rune('a'), screen[2, 0].Value);
        // 第 3 视觉行含 b 段尾 4 字符（尾部可见 — 旧右界截断不可见）
        Assert.Equal(new Rune('b'), screen[3, 0].Value);
        Assert.Equal(new Rune('b'), screen[3, 3].Value);
        Assert.Equal(new Rune('─'), screen[4, 0].Value);
    }

    /// <summary>光标在折行后段仍可见（旧行为光标超右界不画）— 反色块落在第 2 视觉行正确列。</summary>
    [Fact]
    public void SoftWrap_CursorOnSecondVisualLine_StillVisible()
    {
        using var screen = new Screen(cols: 20, rows: 10);
        var box = new PromptInputBox();
        var text = new string('a', 20) + "bc";

        box.Render(screen, new Rect(0, 0, 20, 5), Theme.Dark, text, cursorCol: 22);

        // 光标在 offset 22 → 第 2 视觉行（顶格）列 22-18=4 处
        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        Assert.Equal(inverse, screen[2, 4].StyleId);
    }

    /// <summary>折行高度公式（视觉行数 + 2 分隔线）— ReplView 布局消费。</summary>
    [Fact]
    public void HeightForText_SoftWrappedLines()
    {
        var text = new string('a', 40);
        // 折行宽度 18 → 3 视觉行 → 高度 5
        Assert.Equal(5, PromptInputBox.HeightForText(text, cols: 20));
        // 短文本零折行 → 单行高度 3（与 HeightFor(1) 一致）
        Assert.Equal(3, PromptInputBox.HeightForText("abc", cols: 20));
        // 逻辑行 + 折行混合：20a 折 2 视觉行 + "xx" 1 视觉行 = 3 视觉行 → 高 5
        Assert.Equal(5, PromptInputBox.HeightForText(new string('a', 20) + "\nxx", cols: 20));
    }

    /// <summary>折行内光标视觉行定位（ReplView.InputCursorPos 投影地基）。</summary>
    [Fact]
    public void LocateVisualCursor_WrappedText()
    {
        var text = new string('a', 20) + "bc";
        var lines = InputLineLayout.Wrap(text, 18);

        var (visualIndex, displayCol) = PromptInputBox.LocateVisualCursor(text, lines, cursorCol: 22);
        Assert.Equal(1, visualIndex);
        Assert.Equal(4, displayCol);
    }
}
