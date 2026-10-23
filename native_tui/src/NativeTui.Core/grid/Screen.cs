namespace NativeTui.Core.Grid;

/// <summary>
/// 终端 cell 网格 — 渲染管线的帧缓冲（对齐基准 ink/screen.ts）。
/// 职责：cols×rows 的 cell 存储、文本写入（宽字符自动占位）、Resize 保留左上、
/// golden 序列化（ToAnsiText — L1 cell golden 测试的期望输出形态）。
/// 线程模型：单线程（MainLoop）持有，跨线程由上层 Painter 投递。
/// </summary>
public sealed class Screen : IDisposable
{
    private Cell[] _cells;

    /// <summary>行级链接区间表（DSG043 P3 — 与 Rows 等长；空表 = 该行无链接）。
    /// 链接是行级原子数据非 cell 属性：链接变化必然伴随该行 cell 文本变化（diff 层天然重写整行），
    /// 无需 cell 级失效通知；Clear 原子清空防帧重画残留。</summary>
    private List<IReadOnlyList<LinkSpan>> _links;

    /// <summary>创建指定尺寸的网格（全空白）。样式池为本帧私有（单缓冲场景）。</summary>
    public Screen(int cols, int rows)
        : this(cols, rows, new StylePool())
    {
    }

    /// <summary>
    /// 创建指定尺寸的网格并共享样式池 — 帧双缓冲场景：前后帧 Screen 必须共享同一池，
    /// 否则同一样式在不同帧拿到不同 styleId，CellDiffer 会产生假 diff（每帧全量重绘）。
    /// </summary>
    public Screen(int cols, int rows, StylePool sharedStyles)
    {
        Cols = cols;
        Rows = rows;
        _cells = CreateBlankCells(cols, rows);
        _links = NewLinkTable(rows);
        Styles = sharedStyles;
    }

    /// <summary>列数。</summary>
    public int Cols { get; private set; }

    /// <summary>行数。</summary>
    public int Rows { get; private set; }

    /// <summary>样式 intern 池（本帧缓冲关联）。</summary>
    public StylePool Styles { get; }

    /// <summary>读写指定 cell（越界返回 Blank，写入被忽略）。</summary>
    public Cell this[int row, int col]
    {
        get
        {
            if (row < 0 || row >= Rows || col < 0 || col >= Cols)
            {
                return Cell.Blank;
            }

            return _cells[row * Cols + col];
        }
        set
        {
            if (row < 0 || row >= Rows || col < 0 || col >= Cols)
            {
                return;
            }

            _cells[row * Cols + col] = value;
        }
    }

    /// <summary>清空全部 cell 为空白（链接表原子清空 — 帧重画防旧链接残留）。</summary>
    public void Clear()
    {
        Array.Fill(_cells, Cell.Blank);
        for (var row = 0; row < _links.Count; row++)
        {
            _links[row] = [];
        }
    }

    /// <summary>写入行级链接区间表（替换式 — 渲染器每帧重写本行区间；越界忽略）。</summary>
    public void SetLinks(int row, IReadOnlyList<LinkSpan> spans)
    {
        if (row < 0 || row >= Rows)
        {
            return;
        }

        _links[row] = spans;
    }

    /// <summary>读取行级链接区间表（越界返回空表 — 消费方零防御成本）。</summary>
    public IReadOnlyList<LinkSpan> Links(int row) =>
        row >= 0 && row < Rows ? _links[row] : [];

    /// <summary>调整尺寸，保留左上角既有内容（基准 resize 语义：先擦后画防闪烁由 diff 层处理）。</summary>
    public void Resize(int cols, int rows)
    {
        if (cols == Cols && rows == Rows)
        {
            return;
        }

        var resized = CreateBlankCells(cols, rows);
        var copyCols = Math.Min(cols, Cols);
        var copyRows = Math.Min(rows, Rows);
        for (var row = 0; row < copyRows; row++)
        {
            Array.Copy(_cells, row * Cols, resized, row * cols, copyCols);
        }

        var resizedLinks = NewLinkTable(rows);
        for (var row = 0; row < copyRows; row++)
        {
            resizedLinks[row] = _links[row];
        }

        _cells = resized;
        _links = resizedLinks;
        Cols = cols;
        Rows = rows;
    }

    /// <summary>新建行级链接表（全部空表 — 与行数等长）。</summary>
    private static List<IReadOnlyList<LinkSpan>> NewLinkTable(int rows)
    {
        var table = new List<IReadOnlyList<LinkSpan>>(rows);
        for (var row = 0; row < rows; row++)
        {
            table.Add(Array.Empty<LinkSpan>());
        }

        return table;
    }

    /// <summary>
    /// 在指定行列写入文本（宽字符占 Wide+SpacerTail 两格，零宽字符跳过，
    /// 宽字符放不下或越过 rightBound 即停止）。返回下一个可写列。
    /// rightBound 为 exclusive 右边界（帧裁剪），默认不限制（屏幕全宽）。
    /// </summary>
    public int WriteText(int row, int col, ReadOnlySpan<char> text, int styleId = 0, int rightBound = int.MaxValue)
    {
        var bound = Math.Min(rightBound, Cols);
        foreach (var rune in text.EnumerateRunes())
        {
            var width = RuneWidth.Of(rune);
            if (width == 0)
            {
                continue;
            }

            if (width == 2)
            {
                if (col + 1 >= bound)
                {
                    break;
                }

                this[row, col] = new Cell(rune, styleId, CellWidth.Wide);
                this[row, col + 1] = new Cell(rune, styleId, CellWidth.SpacerTail);
                col += 2;
            }
            else
            {
                if (col >= bound)
                {
                    break;
                }

                this[row, col] = new Cell(rune, styleId, CellWidth.Default);
                col++;
            }
        }

        return col;
    }

    /// <summary>
    /// 序列化为带 SGR 的 ANSI 文本（L1 golden 期望形态）：
    /// 样式变化处插入 transition，SpacerTail 跳过，行尾默认空白（空格+默认样式）裁剪，
    /// 行尾为非 Default 样式时发重置序列。
    /// </summary>
    public string ToAnsiText()
    {
        var builder = new System.Text.StringBuilder();
        for (var row = 0; row < Rows; row++)
        {
            if (row > 0)
            {
                builder.Append('\n');
            }

            var lastContentCol = FindLastContentColumn(row);
            var currentStyle = 0;
            for (var col = 0; col <= lastContentCol; col++)
            {
                var cell = _cells[row * Cols + col];
                if (cell.Width == CellWidth.SpacerTail)
                {
                    continue;
                }

                if (cell.StyleId != currentStyle)
                {
                    builder.Append(Styles.Transition(currentStyle, cell.StyleId));
                    currentStyle = cell.StyleId;
                }

                builder.Append(cell.Value.ToString());
            }

            if (currentStyle != 0)
            {
                builder.Append(Styles.Transition(currentStyle, 0));
            }
        }

        return builder.ToString();
    }

    private int FindLastContentColumn(int row)
    {
        for (var col = Cols - 1; col >= 0; col--)
        {
            if (!_cells[row * Cols + col].Equals(Cell.Blank))
            {
                return col;
            }
        }

        return -1;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        // 数组交给 GC，无句柄需要释放；实现 IDisposable 仅为统一 using 惯例
    }

    private static Cell[] CreateBlankCells(int cols, int rows)
    {
        var cells = new Cell[cols * rows];
        Array.Fill(cells, Cell.Blank);
        return cells;
    }
}
