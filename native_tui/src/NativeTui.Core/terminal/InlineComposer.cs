namespace NativeTui.Core.Terminal;

/// <summary>内容坐标光标位置（列/行，行相对帧内容顶部；inline 模式下内容顶可滚入终端 scrollback）。</summary>
public readonly record struct CursorPos(int Col, int Row);

/// <summary>
/// inline（非 alt-screen）帧组装器 — 对齐基准 ink/log-update.ts 主屏渲染语义：
/// ① 相对光标操作（CR/LF/CUU/CUB/CUF）— 终端滚动后绝对坐标不可知，只有相对量可靠；
/// ② 增长行 append（LF 前进）自然滚入终端 scrollback 成为原生历史（可被终端选择复制）；
/// ③ 收缩清底部行（EraseLine）；④ 变更落在 scrollback 区（不可达）→ fullReset
/// （EraseScreen 重绘可见区）；⑤ 首帧不清屏（保留终端既有历史）；⑥ 零变化零光标移动 → 零输出。
/// 帧尾光标由调用方指定（frameEndCursor，通常为输入框光标位），返回 nextCursor 供下帧传入。
/// </summary>
public sealed class InlineComposer
{
    /// <summary>
    /// 组装 inline 帧输出。prev 为 null 表示首帧（从当前终端行起画，不清屏）；
    /// viewportRows 为终端视口行数（推算 scrollback 深度）；prevCursor 为上一帧尾光标
    /// （内容坐标），frameEndCursor 为本帧尾目标光标。
    /// </summary>
    public string Compose(Screen? prev, Screen next, int viewportRows, CursorPos prevCursor, CursorPos frameEndCursor, out CursorPos nextCursor)
    {
        var builder = new StringBuilder();
        var cursor = prevCursor;

        if (prev is null)
        {
            // 首帧光标物理列未知（终端提示符后任意列），无条件 CR 归零起点
            builder.Append('\r');
            cursor = new CursorPos(0, cursor.Row);
            RenderSlice(builder, next, 0, next.Rows, ref cursor);
        }
        else if (prev.Cols != next.Cols || HasScrollbackChange(prev, next, Math.Max(0, prev.Rows - viewportRows)))
        {
            FullReset(builder, next, viewportRows, ref cursor);
        }
        else
        {
            if (next.Rows < prev.Rows)
            {
                ClearBottomRows(builder, next.Rows, prev.Rows, ref cursor);
            }

            foreach (var (row, startCol, endCol) in CellDiffer.ScanSegments(prev, next))
            {
                MoveTo(builder, ref cursor, new CursorPos(startCol, row));
                AppendStyledCells(builder, next, row, startCol, endCol, ref cursor);
            }

            if (next.Rows > prev.Rows)
            {
                RenderSlice(builder, next, prev.Rows, next.Rows, ref cursor);
            }
        }

        MoveTo(builder, ref cursor, frameEndCursor);
        nextCursor = frameEndCursor;
        return builder.ToString();
    }

    /// <summary>变更是否落在 scrollback 区（行 y &lt; scrollbackRows 且有 cell 差）— 不可达必须 fullReset。</summary>
    private static bool HasScrollbackChange(Screen prev, Screen next, int scrollbackRows)
    {
        if (scrollbackRows <= 0)
        {
            return false;
        }

        return CellDiffer.ScanSegments(prev, next).Any(segment => segment.Row < scrollbackRows);
    }

    /// <summary>fullReset：光标相对移到视口顶 → EraseScreen → 重绘可见区（scrollback 历史保留不动）。</summary>
    private static void FullReset(StringBuilder builder, Screen next, int viewportRows, ref CursorPos cursor)
    {
        var top = Math.Max(0, next.Rows - viewportRows);
        MoveTo(builder, ref cursor, new CursorPos(0, top));
        builder.Append(Csi.EraseScreen());
        cursor = new CursorPos(0, top);
        RenderSlice(builder, next, top, next.Rows, ref cursor);
    }

    /// <summary>
    /// 清底部行 [firstClearedRow, prevRows) — 光标移到被清首行，逐行 EraseLine + LF 行进
    /// （最后行不 LF，防止多余滚屏）；光标停在最后被清行行首。
    /// </summary>
    private static void ClearBottomRows(StringBuilder builder, int firstClearedRow, int prevRows, ref CursorPos cursor)
    {
        MoveTo(builder, ref cursor, new CursorPos(0, firstClearedRow));
        for (var row = firstClearedRow; row < prevRows; row++)
        {
            builder.Append(Csi.EraseLine());
            if (row + 1 < prevRows)
            {
                builder.Append("\r\n");
            }
        }

        cursor = new CursorPos(0, prevRows - 1);
    }

    /// <summary>
    /// 渲染行切片 [startRow, endRow)：每行 CR+LF 前进到目标行（LF 在视口底滚屏创建新行 —
    /// CUD 无法创建行）、行内写到内容末列、行尾 CR+LF（对齐基准 renderFrameSlice 每行收尾）。
    /// </summary>
    private static void RenderSlice(StringBuilder builder, Screen next, int startRow, int endRow, ref CursorPos cursor)
    {
        for (var row = startRow; row < endRow; row++)
        {
            if (cursor.Row < row)
            {
                MoveTo(builder, ref cursor, new CursorPos(0, row));
            }
            else if (cursor.Col > 0)
            {
                builder.Append('\r');
                cursor = new CursorPos(0, row);
            }

            var lastContent = FindLastContentColumn(next, row);
            if (lastContent >= 0)
            {
                AppendStyledCells(builder, next, row, 0, lastContent, ref cursor);
            }

            builder.Append("\r\n");
            cursor = new CursorPos(0, row + 1);
        }
    }

    /// <summary>相对光标移动：跨行 CR + CUU/LF（向下必用 LF — 视口底滚屏创建行）+ CUF；同行 CUF/CUB。</summary>
    private static void MoveTo(StringBuilder builder, ref CursorPos cursor, CursorPos target)
    {
        if (cursor.Row != target.Row)
        {
            builder.Append('\r');
            var dy = target.Row - cursor.Row;
            if (dy > 0)
            {
                builder.Append('\n', dy);
            }
            else
            {
                builder.Append(Csi.CursorUp(-dy));
            }

            if (target.Col > 0)
            {
                builder.Append(Csi.CursorForward(target.Col));
            }
        }
        else
        {
            var dx = target.Col - cursor.Col;
            if (dx > 0)
            {
                builder.Append(Csi.CursorForward(dx));
            }
            else if (dx < 0)
            {
                builder.Append(Csi.CursorBackward(-dx));
            }
        }

        cursor = target;
    }

    /// <summary>写样式化 cell 段（SpacerTail 跳过 — 宽字符本体打印自动推进两列；段尾非默认样式重置）。</summary>
    private static void AppendStyledCells(StringBuilder builder, Screen next, int row, int startCol, int endCol, ref CursorPos cursor)
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
            cursor = new CursorPos(cursor.Col + (cell.Width == CellWidth.Wide ? 2 : 1), cursor.Row);
        }

        if (currentStyle != 0)
        {
            builder.Append(next.Styles.Transition(currentStyle, 0));
        }
    }

    /// <summary>行内最后非空列（无内容返回 -1）。</summary>
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
