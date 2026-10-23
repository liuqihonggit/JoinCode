namespace NativeTui.Core.Repl;

/// <summary>视觉行（软折行投影单元）— LogicalLine 逻辑行号、StartOffset 行内 char 起点、Length 行内 char 长。</summary>
public sealed record VisualLine(int LogicalLine, int StartOffset, int Length);

/// <summary>
/// 输入区软折行投影（DSG044 L3 — 基准 useTextInput Cursor.fromText(value, columns) 折行模型）：
/// 每逻辑行按可用列宽折为视觉行 — 词边界贪心（空格处可断、空格留上行尾）+ grapheme 宽度精确列
///（消费 RuneWidth.WidthOfGrapheme）+ 无断点硬切（CJK 逐字/长 token）。
/// 零折行时视觉行数 = 逻辑行数（单行降级兼容 — 既有渲染行为不变）。
/// 纯投影 — 不改变文本内容与提交语义（折行是渲染层概念）。
/// </summary>
public static class InputLineLayout
{
    /// <summary>整段文本软折行：逐逻辑行折为视觉行（\n 为逻辑行边界，不出现在视觉行内）。</summary>
    public static List<VisualLine> Wrap(string text, int availableCols)
    {
        var result = new List<VisualLine>();
        var logicalLine = 0;
        var lineStart = 0;
        while (lineStart <= text.Length)
        {
            var lineEnd = text.IndexOf('\n', lineStart);
            if (lineEnd < 0)
            {
                lineEnd = text.Length;
            }

            WrapLogicalLine(text.AsSpan(lineStart, lineEnd - lineStart), availableCols, logicalLine, result);
            logicalLine++;
            if (lineEnd >= text.Length)
            {
                break;
            }

            lineStart = lineEnd + 1;
        }

        return result;
    }

    /// <summary>视觉行总显示宽度（渲染列计算 — grapheme 整块宽）。</summary>
    public static int DisplayWidth(ReadOnlySpan<char> lineText)
    {
        var width = 0;
        var pos = 0;
        while (GraphemeSegmenter.TryNext(lineText, pos, out var end))
        {
            width += RuneWidth.WidthOfGrapheme(lineText[pos..end]);
            pos = end;
        }

        return width;
    }

    /// <summary>单逻辑行贪心折行：词边界（空格后可断、空格留上行尾；硬切段空格吞噬不占行首）
    /// + grapheme 宽度 + 无断点硬切。</summary>
    private static void WrapLogicalLine(ReadOnlySpan<char> span, int availableCols, int logicalLine, List<VisualLine> result)
    {
        if (availableCols <= 0)
        {
            result.Add(new VisualLine(logicalLine, 0, span.Length));
            return;
        }

        var width = 0;
        var segmentStart = 0;
        var lastBreak = -1;
        var pos = 0;
        while (GraphemeSegmenter.TryNext(span, pos, out var next))
        {
            var graphemeWidth = RuneWidth.WidthOfGrapheme(span[pos..next]);
            if (width + graphemeWidth > availableCols && pos > segmentStart)
            {
                var breakAt = lastBreak > segmentStart ? lastBreak : pos;
                var swallowedSpace = false;
                if (breakAt == pos && span[pos] == ' ')
                {
                    // 硬切段当前是空格 — 吞噬（空格不占下行行首，词折行惯例）
                    breakAt = next;
                    swallowedSpace = true;
                }

                result.Add(new VisualLine(logicalLine, segmentStart, breakAt - segmentStart));
                segmentStart = breakAt;
                width = swallowedSpace
                    ? 0
                    : segmentStart <= pos
                        ? DisplayWidth(span[segmentStart..pos]) + graphemeWidth
                        : graphemeWidth;
                lastBreak = -1;
            }
            else
            {
                width += graphemeWidth;
            }

            if (span[pos] == ' ')
            {
                lastBreak = next;
            }

            pos = next;
        }

        result.Add(new VisualLine(logicalLine, segmentStart, span.Length - segmentStart));
    }
}
