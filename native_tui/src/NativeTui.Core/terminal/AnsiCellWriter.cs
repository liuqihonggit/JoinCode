namespace NativeTui.Core.Terminal;

/// <summary>
/// ANSI cell 段输出公共件（DSG043 P3）— CellDiffer（绝对定位差分）与 InlineComposer
/// （相对光标差分）共享的 cell→ANSI 拼装：样式 transition 切换 + OSC 8 链接区间边界注入
///（进入区间首 cell 前插 Hyperlink(uri)、出区间后插 HyperlinkEnd — 与 SGR 正交同流，
/// 对齐基准 ink Link 在终端流上的独立区间标记）；不支持终端零注入纯文本降级。
/// 光标列推进（ref cursorCol）由调用方消费（InlineComposer 需相对光标跟踪；CellDiffer 传弃值）。
/// </summary>
internal static class AnsiCellWriter
{
    /// <summary>写样式化 cell 段（SpacerTail 跳过 — 宽字符本体打印自动推进两列；段尾非默认样式重置；
    /// 链接区间边界按 cell 起始列判定注入 OSC 8 开/闭对）。</summary>
    /// <param name="builder">ANSI 输出流。</param>
    /// <param name="next">目标帧（cell + 行级链接表来源）。</param>
    /// <param name="row">行号。</param>
    /// <param name="startCol">段起始列（含）。</param>
    /// <param name="endCol">段结束列（含）。</param>
    /// <param name="hyperlinksSupported">超链接支持（false = 零注入降级）。</param>
    /// <param name="cursorCol">光标列跟踪（ref — 调用方消费或传弃值）。</param>
    public static void AppendStyledCells(
        StringBuilder builder,
        Screen next,
        int row,
        int startCol,
        int endCol,
        bool hyperlinksSupported,
        ref int cursorCol)
    {
        var currentStyle = 0;
        var openLink = -1;
        var links = hyperlinksSupported ? next.Links(row) : [];
        for (var col = startCol; col <= endCol; col++)
        {
            var cell = next[row, col];
            if (cell.Width == CellWidth.SpacerTail)
            {
                continue;
            }

            var linkIndex = LinkIndexAt(links, col);
            if (linkIndex != openLink)
            {
                if (openLink >= 0)
                {
                    builder.Append(Osc.HyperlinkEnd());
                    openLink = -1;
                }

                if (linkIndex >= 0)
                {
                    builder.Append(Osc.Hyperlink(links[linkIndex].Url));
                    openLink = linkIndex;
                }
            }

            if (cell.StyleId != currentStyle)
            {
                builder.Append(next.Styles.Transition(currentStyle, cell.StyleId));
                currentStyle = cell.StyleId;
            }

            builder.Append(cell.Value.ToString());
            cursorCol += cell.Width == CellWidth.Wide ? 2 : 1;
        }

        if (openLink >= 0)
        {
            builder.Append(Osc.HyperlinkEnd());
        }

        if (currentStyle != 0)
        {
            builder.Append(next.Styles.Transition(currentStyle, 0));
        }
    }

    /// <summary>列所属链接区间索引（-1 = 无；cell 起始列判定 — Wide 占两列归本体列）。</summary>
    private static int LinkIndexAt(IReadOnlyList<LinkSpan> links, int col)
    {
        for (var i = 0; i < links.Count; i++)
        {
            if (col >= links[i].StartCol && col < links[i].EndCol)
            {
                return i;
            }
        }

        return -1;
    }
}
