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
    /// 渲染到指定帧区域：row 0 顶部分隔线，row 1 提示符 + 文本 + 光标，row 2 底部分隔线。
    /// cursorCol 为文本内偏移（0 = 首字符之前），光标屏幕列 = frame.X + PromptWidth + cursorCol。
    /// </summary>
    public void Render(Screen screen, Rect frame, Theme theme, string text, int cursorCol, bool busy = false)
    {
        var border = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.PromptBorder });
        BorderPainter.DrawRule(screen, frame.Y, frame.X, frame.Width, border);
        BorderPainter.DrawRule(screen, frame.Y + 2, frame.X, frame.Width, border);

        var promptStyle = busy ? screen.Styles.GetOrAdd(new TextStyle { Dim = true }) : 0;
        var row = frame.Y + 1;
        screen.WriteText(row, frame.X, $"{Figures.Pointer} ", promptStyle, frame.Right);
        screen.WriteText(row, frame.X + PromptWidth, text, 0, frame.Right);
        DrawCursor(screen, frame, row, cursorCol);
    }

    /// <summary>在光标屏幕列写入反色 cell（字符处反色该字符；行尾/空白处为反色空格块）。</summary>
    private static void DrawCursor(Screen screen, Rect frame, int row, int cursorCol)
    {
        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        var col = frame.X + PromptWidth + cursorCol;
        if (col < 0 || col >= frame.Right || col >= screen.Cols)
        {
            return;
        }

        screen[row, col] = screen[row, col] with { StyleId = inverse };
    }
}
