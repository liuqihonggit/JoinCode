namespace NativeTui.Tests.Repl;

/// <summary>
/// 宽字符输入渲染测试（修复：中文输入光标/ghost 错位）— CursorCol 是 UTF-16 char 偏移，
/// 屏幕列须按显示宽度换算（CJK 每字 2 列）；PromptInputBox 光标列与 ghost 起点均须宽度换算
///（原实现 cursorCol/text.Length 直当屏幕列，输入"你好"光标落进字符中间）。
/// </summary>
public sealed class WideCharRenderTests
{
    private const int FrameX = 0;
    private const int FrameY = 5;

    /// <summary>显示宽度换算 — 中文 2 列/字，ASCII 1 列/字。</summary>
    [Theory]
    [InlineData("abc", 3, 3)]
    [InlineData("你好", 2, 4)]
    [InlineData("a你b", 3, 4)]
    public void DisplayWidth_MixedText_AccumulatesRuneWidth(string text, int charCount, int expectedWidth)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
            width += RuneWidth.Of(rune);

        Assert.Equal(charCount, text.Length);
        Assert.Equal(expectedWidth, width);
    }

    /// <summary>光标屏幕列 — 中文文本光标偏移按显示宽度定位（"你好"光标在末尾 = PromptWidth+4 非 +2）。</summary>
    [Fact]
    public void PromptInputBox_CursorColumn_UsesDisplayWidth()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var box = new PromptInputBox();

        box.Render(screen, new Rect(FrameX, FrameY, 40, 3), Theme.Dark, "你好", cursorCol: 2);

        // "你"占 PromptWidth..+1（Wide+SpacerTail），"好"占 +2..+3，光标在 +4（反色块）
        Assert.Equal(CellWidth.Wide, screen[FrameY + 1, PromptInputBox.PromptWidth].Width);
        Assert.Equal(CellWidth.SpacerTail, screen[FrameY + 1, PromptInputBox.PromptWidth + 1].Width);
        Assert.Equal(CellWidth.Wide, screen[FrameY + 1, PromptInputBox.PromptWidth + 2].Width);
        Assert.Equal(CellWidth.SpacerTail, screen[FrameY + 1, PromptInputBox.PromptWidth + 3].Width);
        // 光标反色块在 +4（原实现错画在 +2 — 落在"好"字符上）
        var inverseStyle = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        Assert.Equal(inverseStyle, screen[FrameY + 1, PromptInputBox.PromptWidth + 4].StyleId);
    }

    /// <summary>ghost 起点 — 中文文本后 ghost 渲染不重叠（起点 = PromptWidth + 显示宽度）。</summary>
    [Fact]
    public void PromptInputBox_GhostStart_AfterWideText()
    {
        using var screen = new Screen(cols: 40, rows: 10);
        var box = new PromptInputBox();

        box.Render(screen, new Rect(FrameX, FrameY, 40, 3), Theme.Dark, "你好", cursorCol: 2, ghostSuffix: "x");

        // ghost 'x' 应在列 PromptWidth+4（宽度换算后），而非 char 数的 +2（会撞"好"）
        Assert.Equal(new Rune('x'), screen[FrameY + 1, PromptInputBox.PromptWidth + 4].Value);
        Assert.Equal(CellWidth.Wide, screen[FrameY + 1, PromptInputBox.PromptWidth + 2].Width);
    }
}
