namespace NativeTui.Core.Repl;

/// <summary>
/// 输入框 — 像素级对齐基准 PromptInput.tsx 标志性样式：
/// 上下全宽纯 ─ 分隔线（borderStyle=round 但 borderLeft/Right=false，角字符为空串）
/// + ❯ 提示符（busy 时 dim，基准 PromptChar）
/// + 文本 + 光标（文本 offset 处字符反色，行尾为反色空格块，基准 Cursor.render）。
/// 多行（基准 multiline）：按 \n 拆逻辑行逐行渲染 — 首行带 ❯ 前缀、续行无前缀顶格；
/// 行数超出 frame 高度时贴底滚动窗口（光标行恒可见）；ghost 挂末行尾；光标跨行定位。
/// 单行降级逐字节兼容：无换行符时渲染输出与单行版完全一致（既有测试零破坏）。
/// </summary>
public sealed class PromptInputBox
{
    /// <summary>提示符前缀宽度（❯ + 1 空格，基准 PromptChar 的单一 Text span）。</summary>
    public const int PromptWidth = 2;

    /// <summary>
    /// 渲染到指定帧区域：row 0 顶部分隔线，row 1 起提示符 + 文本 + 光标（+ ghost 幽灵后缀），末行底部分隔线。
    /// 单行：row 1 文本 + 光标 + ghost，row 2 底线（与既有行为逐字节一致）。
    /// 多行：首行 "❯ " + 文本，续行顶格无前缀（基准 multiline 续行语义）；行数超 frame.Height-2 时
    /// 贴底显示最后可见行（光标行恒在窗口内）；ghost 渲染在末可见行之后。
    /// cursorCol 为文本内偏移（0 = 首字符之前），光标屏幕列 = 行首列 + 显示宽度
    ///（修复：中文宽字符 — char 偏移须换算显示列，原直接当屏幕列致光标落进字符中间）。
    /// ghostSuffix 非空时以 dim 色渲染在末行文本之后（显示宽度换算起点，帧右界截断 — 基准 ghost text dim 提示）。
    /// </summary>
    public void Render(Screen screen, Rect frame, Theme theme, string text, int cursorCol, bool busy = false, string ghostSuffix = "")
    {
        var border = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.PromptBorder });
        BorderPainter.DrawRule(screen, frame.Y, frame.X, frame.Width, border);
        BorderPainter.DrawRule(screen, frame.Y + frame.Height - 1, frame.X, frame.Width, border);

        var promptStyle = busy ? screen.Styles.GetOrAdd(new TextStyle { Dim = true }) : 0;
        var lines = SplitLogicalLines(text);
        var topLine = FirstVisibleLine(lines.Count, Math.Max(1, frame.Height - 2), CursorLineIndex(text, cursorCol));
        RenderTextLines(screen, frame, lines, topLine, promptStyle);
        RenderGhostSuffix(screen, frame, lines, topLine, ghostSuffix);
        DrawCursor(screen, frame, lines, topLine, text, cursorCol);
    }

    /// <summary>输入框总高度（顶/底分隔线 + 文本行数）— 接线方按逻辑行数分配 frame 高度。</summary>
    public static int HeightFor(int lineCount) => Math.Max(1, lineCount) + 2;

    /// <summary>按 \n 拆逻辑行（空文本为单空行）。</summary>
    internal static List<string> SplitLogicalLines(string text)
    {
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines.Add(text[start..i]);
                start = i + 1;
            }
        }

        lines.Add(text[start..]);
        return lines;
    }

    /// <summary>可见窗口首行 — 贴底优先（显示末尾 maxVisible 行），光标行越窗时滑动窗口保光标可见。</summary>
    internal static int FirstVisibleLine(int lineCount, int maxVisible, int cursorLine)
    {
        var top = Math.Max(0, lineCount - maxVisible);
        top = Math.Min(top, cursorLine);
        top = Math.Max(top, cursorLine - maxVisible + 1);
        return Math.Max(0, top);
    }

    /// <summary>光标所在逻辑行索引（光标前的换行符计数；偏移钳制）。</summary>
    internal static int CursorLineIndex(string text, int cursorCol)
    {
        var clamped = Math.Clamp(cursorCol, 0, text.Length);
        var line = 0;
        for (var i = 0; i < clamped; i++)
        {
            if (text[i] == '\n')
            {
                line++;
            }
        }

        return line;
    }

    /// <summary>逐行渲染文本 — 首行（line 0）带 ❯ 前缀，续行顶格无前缀；可见窗口外的行不画。</summary>
    private static void RenderTextLines(Screen screen, Rect frame, List<string> lines, int topLine, int promptStyle)
    {
        var maxVisible = Math.Max(1, frame.Height - 2);
        for (var i = topLine; i < lines.Count && i - topLine < maxVisible; i++)
        {
            var row = frame.Y + 1 + (i - topLine);
            if (i == 0)
            {
                screen.WriteText(row, frame.X, $"{Figures.Pointer} ", promptStyle, frame.Right);
                screen.WriteText(row, frame.X + PromptWidth, lines[i], 0, frame.Right);
            }
            else
            {
                screen.WriteText(row, frame.X, lines[i], 0, frame.Right);
            }
        }
    }

    /// <summary>ghost 幽灵后缀 — dim 色渲染在末可见行文本之后（单行时与既有行为一致）。</summary>
    private static void RenderGhostSuffix(Screen screen, Rect frame, List<string> lines, int topLine, string ghostSuffix)
    {
        if (ghostSuffix.Length == 0)
        {
            return;
        }

        var maxVisible = Math.Max(1, frame.Height - 2);
        var lastVisible = Math.Min(lines.Count - 1, topLine + maxVisible - 1);
        var row = frame.Y + 1 + (lastVisible - topLine);
        var baseCol = lastVisible == 0 ? frame.X + PromptWidth : frame.X;
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(row, baseCol + MeasureDisplayWidth(lines[lastVisible]), ghostSuffix, dim, frame.Right);
    }

    /// <summary>在光标屏幕位置写入反色 cell（字符处反色该字符；行尾/空白处为反色空格块）—
    /// 多行：光标行 = 行索引定位行号，行内列 = 显示宽度换算（首行带前缀偏移，续行顶格）。</summary>
    private static void DrawCursor(Screen screen, Rect frame, List<string> lines, int topLine, string text, int cursorCol)
    {
        var cursorLine = CursorLineIndex(text, cursorCol);
        if (cursorLine < topLine)
        {
            return;
        }

        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        var row = frame.Y + 1 + (cursorLine - topLine);
        var columnInLine = Math.Clamp(cursorCol, 0, text.Length) - LineStartOffsetOf(text, cursorLine);
        var lineText = lines[Math.Min(cursorLine, lines.Count - 1)];
        var col = (cursorLine == 0 ? frame.X + PromptWidth : frame.X)
            + MeasureDisplayWidth(lineText.AsSpan(0, Math.Min(columnInLine, lineText.Length)));
        if (col < 0 || col >= frame.Right || col >= screen.Cols)
        {
            return;
        }

        screen[row, col] = screen[row, col] with { StyleId = inverse };
    }

    /// <summary>指定逻辑行行首偏移（行索引内换行符扫描）。</summary>
    private static int LineStartOffsetOf(string text, int line)
    {
        var offset = 0;
        var current = 0;
        while (current < line && offset < text.Length)
        {
            if (text[offset] == '\n')
            {
                current++;
            }

            offset++;
        }

        return offset;
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
