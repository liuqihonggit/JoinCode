namespace NativeTui.Core.Diff;

/// <summary>
/// 逐 cell 帧差分渲染器 — 对齐基准 ink/log-update.ts 的 VirtualScreen 语义。
/// 规则：
/// ① 首帧/尺寸变化 → 清屏全量绘制；
/// ② 否则逐 cell 比较，连续变化合并为段，段首绝对定位（CursorPosition），段内样式变化插 transition；
/// ③ next 的 SpacerTail 不输出（终端打印宽字符自动推进光标两格）；
/// ④ 段尾若停在非默认样式发重置（每段从 Default 样式状态起算，免跨段状态跟踪）；
/// ⑤ 无变化返回空字符串（零输出）。
/// DEC 2026 原子包裹由上层 TerminalWriter 组帧时统一加。
/// </summary>
public sealed class CellDiffer
{
    /// <summary>渲染从 prev 到 next 的帧更新序列。prev 为 null 表示首帧。</summary>
    public string RenderFrame(Screen? prev, Screen next)
    {
        if (prev is null || prev.Cols != next.Cols || prev.Rows != next.Rows)
        {
            return RenderFullFrame(next);
        }

        return RenderDiffFrame(prev, next);
    }

    private static string RenderFullFrame(Screen next)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(Csi.EraseScreen());
        for (var row = 0; row < next.Rows; row++)
        {
            AppendRowContent(builder, next, row, positionPrefix: true);
        }

        return builder.ToString();
    }

    private static string RenderDiffFrame(Screen prev, Screen next)
    {
        var builder = new System.Text.StringBuilder();
        for (var row = 0; row < next.Rows; row++)
        {
            var col = 0;
            while (col < next.Cols)
            {
                if (prev[row, col].Equals(next[row, col]))
                {
                    col++;
                    continue;
                }

                var segmentStart = col;
                while (col < next.Cols && !prev[row, col].Equals(next[row, col]))
                {
                    col++;
                }

                AppendSegment(builder, next, row, segmentStart, col - 1);
            }
        }

        return builder.ToString();
    }

    private static void AppendSegment(StringBuilder builder, Screen next, int row, int startCol, int endCol)
    {
        builder.Append(Csi.CursorPosition(row + 1, startCol + 1));
        AppendStyledCells(builder, next, row, startCol, endCol);
    }

    private static void AppendRowContent(StringBuilder builder, Screen next, int row, bool positionPrefix)
    {
        var lastContent = FindLastContentColumn(next, row);
        if (lastContent < 0)
        {
            return;
        }

        if (positionPrefix)
        {
            builder.Append(Csi.CursorPosition(row + 1, 1));
        }

        AppendStyledCells(builder, next, row, 0, lastContent);
    }

    private static void AppendStyledCells(StringBuilder builder, Screen next, int row, int startCol, int endCol)
    {
        var currentStyle = 0;
        for (var col = startCol; col <= endCol; col++)
        {
            var cell = next[row, col];
            if (cell.Width == CellWidth.SpacerTail)
            {
                continue;
            }

            if (cell.StyleId != currentStyle)
            {
                builder.Append(next.Styles.Transition(currentStyle, cell.StyleId));
                currentStyle = cell.StyleId;
            }

            builder.Append(cell.Value.ToString());
        }

        if (currentStyle != 0)
        {
            builder.Append(next.Styles.Transition(currentStyle, 0));
        }
    }

    private static int FindLastContentColumn(Screen screen, int row)
    {
        for (var col = screen.Cols - 1; col >= 0; col--)
        {
            if (!screen[row, col].Equals(Cell.Blank))
            {
                return col;
            }
        }

        return -1;
    }
}
