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
    /// 软折行（DSG044 L3 — 基准 Cursor.fromText 折行模型）：单逻辑行超宽按 contentCols =
    /// frame.Width - PromptWidth 折为多视觉行（词边界贪心 + grapheme 宽度），视觉行续行顶格；
    /// 视觉行数超出 frame 高度时贴底滚动窗口（光标行恒可见）。
    /// 首视觉行（视觉行 0 = 逻辑行 0 首视觉行）带 ❯ 前缀，其余视觉行顶格无前缀；
    /// 单行降级逐字节兼容：无折行无换行时渲染输出与单行版完全一致（既有测试零破坏）。
    /// cursorCol 为文本内偏移（0 = 首字符之前），经 LocateVisualCursor 投影到视觉行/列
    ///（光标在折行后段仍可见 — 旧右界截断行为废弃）。
    /// ghostSuffix 非空时以 dim 色渲染在末可见视觉行文本之后。
    /// </summary>
    public void Render(Screen screen, Rect frame, Theme theme, string text, int cursorCol, bool busy = false, string ghostSuffix = "")
    {
        var border = screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.PromptBorder });
        BorderPainter.DrawRule(screen, frame.Y, frame.X, frame.Width, border);
        BorderPainter.DrawRule(screen, frame.Y + frame.Height - 1, frame.X, frame.Width, border);

        var promptStyle = busy ? screen.Styles.GetOrAdd(new TextStyle { Dim = true }) : 0;
        var contentCols = Math.Max(1, frame.Width - PromptWidth);
        var visualLines = InputLineLayout.Wrap(text, contentCols);
        var (cursorVisual, cursorDisplayCol) = LocateVisualCursor(text, visualLines, cursorCol);
        var topLine = FirstVisibleLine(visualLines.Count, Math.Max(1, frame.Height - 2), cursorVisual);
        RenderTextLines(screen, frame, text, visualLines, topLine, promptStyle);
        RenderGhostSuffix(screen, frame, text, visualLines, topLine, ghostSuffix);
        DrawCursor(screen, frame, visualLines, topLine, cursorVisual, cursorDisplayCol);
    }

    /// <summary>输入框总高度（视觉行数 + 2 分隔线）— ReplView 布局按终端宽折行后分配 frame 高度
    ///（DSG044 L3 软折行高度公式；cols = frame 总宽，折行内容宽 = cols - PromptWidth）。</summary>
    public static int HeightForText(string text, int cols) =>
        HeightFor(InputLineLayout.Wrap(text, Math.Max(1, cols - PromptWidth)).Count);

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

    /// <summary>逐视觉行渲染 — 首视觉行（index 0）带 ❯ 前缀，其余视觉行顶格无前缀；可见窗口外的行不画。</summary>
    private static void RenderTextLines(Screen screen, Rect frame, string text, List<VisualLine> visualLines, int topLine, int promptStyle)
    {
        var logicalLines = SplitLogicalLines(text);
        var maxVisible = Math.Max(1, frame.Height - 2);
        for (var i = topLine; i < visualLines.Count && i - topLine < maxVisible; i++)
        {
            var row = frame.Y + 1 + (i - topLine);
            var visual = visualLines[i];
            var segment = logicalLines[visual.LogicalLine].Substring(visual.StartOffset, visual.Length);
            if (i == 0)
            {
                screen.WriteText(row, frame.X, $"{Figures.Pointer} ", promptStyle, frame.Right);
                screen.WriteText(row, frame.X + PromptWidth, segment, 0, frame.Right);
            }
            else
            {
                screen.WriteText(row, frame.X, segment, 0, frame.Right);
            }
        }
    }

    /// <summary>ghost 幽灵后缀 — dim 色渲染在末可见视觉行文本之后（单行时与既有行为一致）。</summary>
    private static void RenderGhostSuffix(Screen screen, Rect frame, string text, List<VisualLine> visualLines, int topLine, string ghostSuffix)
    {
        if (ghostSuffix.Length == 0)
        {
            return;
        }

        var logicalLines = SplitLogicalLines(text);
        var maxVisible = Math.Max(1, frame.Height - 2);
        var lastVisible = Math.Min(visualLines.Count - 1, topLine + maxVisible - 1);
        var row = frame.Y + 1 + (lastVisible - topLine);
        var visual = visualLines[lastVisible];
        var segment = logicalLines[visual.LogicalLine].Substring(visual.StartOffset, visual.Length);
        var baseCol = lastVisible == 0 ? frame.X + PromptWidth : frame.X;
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(row, baseCol + MeasureDisplayWidth(segment), ghostSuffix, dim, frame.Right);
    }

    /// <summary>在光标屏幕位置写入反色 cell（字符处反色该字符；行尾/空白处为反色空格块）—
    /// 视觉行投影（DSG044 L3）：光标行 = 视觉行索引定位行号，行内列 = 视觉行内显示列
    ///（首视觉行带前缀偏移，其余视觉行顶格；折行后段光标仍可见）。</summary>
    private static void DrawCursor(Screen screen, Rect frame, List<VisualLine> visualLines, int topLine, int cursorVisual, int cursorDisplayCol)
    {
        if (cursorVisual < topLine)
        {
            return;
        }

        var inverse = screen.Styles.GetOrAdd(new TextStyle { Inverse = true });
        var row = frame.Y + 1 + (cursorVisual - topLine);
        var col = (cursorVisual == 0 ? frame.X + PromptWidth : frame.X) + cursorDisplayCol;
        if (col < 0 || col >= frame.Right || col >= screen.Cols)
        {
            return;
        }

        screen[row, col] = screen[row, col] with { StyleId = inverse };
    }

    /// <summary>光标视觉行投影（DSG044 L3 — ReplView.InputCursorPos 地基）：cursorCol（char 偏移）
    /// → (视觉行索引, 视觉行内显示列)。视觉行全局 char 区间 = 逻辑行起点 + 视觉行 StartOffset；
    /// 行尾边界归属下一视觉行（软折行断点处行尾 = 下行首同 char 位 — 光标物理在下侧行首，
    /// 末视觉行行尾归属自身）；行内列 = 行首至光标的 grapheme 显示宽度。</summary>
    internal static (int VisualIndex, int DisplayCol) LocateVisualCursor(string text, List<VisualLine> visualLines, int cursorCol)
    {
        var clamped = Math.Clamp(cursorCol, 0, text.Length);
        var logicalLines = SplitLogicalLines(text);
        var logicalStarts = new int[logicalLines.Count + 1];
        for (var i = 0; i < logicalLines.Count; i++)
        {
            logicalStarts[i + 1] = logicalStarts[i] + logicalLines[i].Length + 1;
        }

        var visualIndex = visualLines.Count - 1;
        var globalStart = logicalStarts[visualLines[visualIndex].LogicalLine] + visualLines[visualIndex].StartOffset;
        for (var i = 0; i < visualLines.Count; i++)
        {
            var start = logicalStarts[visualLines[i].LogicalLine] + visualLines[i].StartOffset;
            var end = start + visualLines[i].Length;
            var ownsCursor = clamped < end || (clamped == end && i == visualLines.Count - 1);
            if (clamped >= start && ownsCursor)
            {
                visualIndex = i;
                globalStart = start;
                break;
            }

            if (start > clamped)
            {
                break;
            }
        }

        var lineText = logicalLines[visualLines[visualIndex].LogicalLine];
        var offsetInVisual = clamped - globalStart;
        var segment = lineText.AsSpan(
            visualLines[visualIndex].StartOffset,
            Math.Min(offsetInVisual, visualLines[visualIndex].Length));
        return (visualIndex, MeasureDisplayWidth(segment));
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
