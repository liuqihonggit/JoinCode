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
    /// <summary>渲染从 prev 到 next 的帧更新序列。prev 为 null 表示首帧（既有入口 — OSC 8 零注入兼容）。</summary>
    public string RenderFrame(Screen? prev, Screen next) =>
        RenderFrame(prev, next, hyperlinksSupported: false);

    /// <summary>渲染从 prev 到 next 的帧更新序列（带 OSC 8 超链接支持 — DSG043 P3；
    /// false = 零注入纯文本降级）。</summary>
    public string RenderFrame(Screen? prev, Screen next, bool hyperlinksSupported)
    {
        if (prev is null || prev.Cols != next.Cols || prev.Rows != next.Rows)
        {
            return RenderFullFrame(next, hyperlinksSupported);
        }

        return RenderDiffFrame(prev, next, hyperlinksSupported);
    }

    private static string RenderFullFrame(Screen next, bool hyperlinksSupported)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(Csi.EraseScreen());
        for (var row = 0; row < next.Rows; row++)
        {
            AppendRowContent(builder, next, row, positionPrefix: true, hyperlinksSupported);
        }

        return builder.ToString();
    }

    private static string RenderDiffFrame(Screen prev, Screen next, bool hyperlinksSupported)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var (row, startCol, endCol) in ScanSegments(prev, next))
        {
            AppendSegment(builder, next, row, startCol, endCol, hyperlinksSupported);
        }

        return builder.ToString();
    }

    /// <summary>
    /// 扫描前后帧的连续变更段（行级合并）— CellDiffer（绝对定位）与 InlineComposer（相对光标）
    /// 共享的差分扫描；段 = [startCol, endCol] 闭区间内 prev/next cell 不全等的连续区间；
    /// 行域取共有行（InlineComposer 场景 prev/next 高度可不同，CellDiffer 场景恒等宽等高）。
    /// </summary>
    internal static IEnumerable<(int Row, int StartCol, int EndCol)> ScanSegments(Screen prev, Screen next)
    {
        for (var row = 0; row < Math.Min(prev.Rows, next.Rows); row++)
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

                yield return (row, segmentStart, col - 1);
            }
        }
    }

    private static void AppendSegment(StringBuilder builder, Screen next, int row, int startCol, int endCol, bool hyperlinksSupported)
    {
        builder.Append(Csi.CursorPosition(row + 1, startCol + 1));
        AppendStyledCells(builder, next, row, startCol, endCol, hyperlinksSupported);
    }

    private static void AppendRowContent(StringBuilder builder, Screen next, int row, bool positionPrefix, bool hyperlinksSupported)
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

        AppendStyledCells(builder, next, row, 0, lastContent, hyperlinksSupported);
    }

    /// <summary>写样式化 cell 段 — 委托 <see cref="AnsiCellWriter"/>（样式 + OSC 8 链接区间注入公共件；
    /// 超链接支持由 <see cref="RenderFrame(Screen, Screen, bool)"/> 重载传入，无参重载保持既有零注入行为）。
    /// 绝对定位差分无需光标跟踪 — cursorCol 传弃值。</summary>
    private static void AppendStyledCells(StringBuilder builder, Screen next, int row, int startCol, int endCol, bool hyperlinksSupported)
    {
        var discardCol = 0;
        AnsiCellWriter.AppendStyledCells(builder, next, row, startCol, endCol, hyperlinksSupported, ref discardCol);
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
