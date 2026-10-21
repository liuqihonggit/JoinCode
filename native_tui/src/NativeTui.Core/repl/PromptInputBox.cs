namespace NativeTui.Core.Repl;

/// <summary>
/// 输入框 — 像素级对齐基准 PromptInput.tsx 标志性样式：
/// 上下全宽纯 ─ 分隔线（borderStyle=round 但 borderLeft/Right=false，角字符为空串）
/// + ❯ 提示符（busy 时 dim，基准 PromptChar）
/// + 文本 + 光标（文本 offset 处字符反色，行尾为反色空格块，基准 Cursor.render）。
/// </summary>
public sealed class PromptInputBox
{
    /// <summary>提示符前缀宽度（❯ + 1 空格，基准 PromptChar 的单一 Text span）。</summary>
    public const int PromptWidth = 2;

    /// <summary>
    /// 渲染到指定帧区域：row 0 顶部分隔线，row 1 提示符 + 文本 + 光标（+ ghost 幽灵后缀），row 2 底部分隔线。
    /// cursorCol 为文本内偏移（0 = 首字符之前），光标屏幕列 = frame.X + PromptWidth + 显示宽度
    ///（修复：中文宽字符 — char 偏移须换算显示列，原直接当屏幕列致光标落进字符中间）。
    /// ghostSuffix 非空时以 dim 色渲染在文本之后（显示宽度换算起点，帧右界截断 — 基准 ghost text dim 提示）。
    /// </summary>
    public void Render(Screen screen, Rect frame, Theme theme, string text, int cursorCol, bool busy = false, string ghostSuffix = "")
    {
        var border = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.PromptBorder });
        BorderPainter.DrawRule(screen, frame.Y, frame.X, frame.Width, border);
        BorderPainter.DrawRule(screen, frame.Y + 2, frame.X, frame.Width, border);

        var promptStyle = busy ? screen.Styles.GetOrAdd(new TextStyle { Dim = true }) : 0;
        var row = frame.Y + 1;
        screen.WriteText(row, frame.X, $"{Figures.Pointer} ", promptStyle, frame.Right);
        screen.WriteText(row, frame.X + PromptWidth, text, 0, frame.Right);
        var textDisplayWidth = MeasureDisplayWidth(text);
        if (ghostSuffix.Length > 0)
        {
            var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            screen.WriteText(row, frame.X + PromptWidth + textDisplayWidth, ghostSuffix, dim, frame.Right);
        }

        DrawCursor(screen, frame, row, text, cursorCol);
    }

    /// <summary>在光标屏幕列写入反色 cell（字符处反色该字符；行尾/空白处为反色空格块）—
    /// char 偏移先换算显示宽度（CJK 每字 2 列）。</summary>
    private static void DrawCursor(Screen screen, Rect frame, int row, string text, int cursorCol)
    {
        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        var col = frame.X + PromptWidth + MeasureDisplayWidth(text.AsSpan(0, Math.Min(cursorCol, text.Length)));
        if (col < 0 || col >= frame.Right || col >= screen.Cols)
        {
            return;
        }

        screen[row, col] = screen[row, col] with { StyleId = inverse };
    }

    /// <summary>文本显示宽度（逐 rune 累加 RuneWidth — CJK/emoji 2 列，组合符 0 列）。</summary>
    internal static int MeasureDisplayWidth(ReadOnlySpan<char> text)
    {
        var width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += RuneWidth.Of(rune);
        }

        return width;
    }
}
