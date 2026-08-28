namespace JoinCode.Vision.Quadtree;

/// <summary>
/// 四叉树编码器 — 纯网格计算，无图像依赖
/// 编码：数字点分路径 L0.2.1，象限序 SW=0/SE=1/NW=2/NE=3（左下起算）
/// </summary>
[Register(typeof(IQuadtreeAnnotator), ServiceLifetime.Singleton)]
public sealed partial class QuadtreeEncoder : IQuadtreeAnnotator
{
    /// <summary>构建指定层数的空网格（所有格子 alpha=-1 隐藏）</summary>
    public QuadtreeGrid BuildGrid(int imageWidth, int imageHeight, int depth)
    {
        if (imageWidth <= 0) throw new ArgumentException("[VIS001] 图片宽度必须为正", nameof(imageWidth));
        if (imageHeight <= 0) throw new ArgumentException("[VIS002] 图片高度必须为正", nameof(imageHeight));
        if (depth < 0) throw new ArgumentException("[VIS003] 层数不能为负", nameof(depth));

        var size = 1 << depth;
        var cellW = imageWidth / size;
        var cellH = imageHeight / size;
        var cells = new List<QuadtreeCell>(size * size);

        for (var row = 0; row < size; row++)
        {
            for (var col = 0; col < size; col++)
            {
                var x = col * cellW;
                var y = row * cellH;
                var code = EncodeFromGrid(col, row, depth);
                var quadrant = GetQuadrantFromCode(code);
                var region = ComputeRegion(x, y, cellW, cellH, imageWidth, imageHeight);
                cells.Add(new QuadtreeCell(code, quadrant, region, x, y, cellW, cellH, -1));
            }
        }

        return new QuadtreeGrid(cells, imageWidth, imageHeight, depth);
    }

    /// <summary>查询格子的八方位邻居编码（同层），边界外返回 null</summary>
    public string? GetNeighbor(string cellCode, CardinalDirection direction, int imageWidth, int imageHeight, int depth)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cellCode);
        if (depth < 0) throw new ArgumentException("[VIS004] 层数不能为负", nameof(depth));

        var (col, row) = DecodeToGrid(cellCode);
        var size = 1 << depth;
        var (dcol, drow) = DirectionToDelta(direction);
        var ncol = col + dcol;
        var nrow = row + drow;

        if (ncol < 0 || nrow < 0 || ncol >= size || nrow >= size) return null;
        return EncodeFromGrid(ncol, nrow, depth);
    }

    /// <summary>批量染色格子（更新 alpha 值），返回新网格实例</summary>
    public QuadtreeGrid PaintCells(QuadtreeGrid grid, IReadOnlyDictionary<string, double> paints)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(paints);
        if (paints.Count == 0) return grid;

        var cells = new List<QuadtreeCell>(grid.Cells.Count);
        foreach (var cell in grid.Cells)
        {
            cells.Add(paints.TryGetValue(cell.Code, out var alpha) ? cell with { Alpha = alpha } : cell);
        }

        return new QuadtreeGrid(cells, grid.ImageWidth, grid.ImageHeight, grid.Depth);
    }

    /// <summary>从网格坐标生成编码 — col/row 高位到低位逐层提取象限序</summary>
    internal static string EncodeFromGrid(int col, int row, int depth)
    {
        if (depth == 0) return "L0";
        var sb = new StringBuilder("L0", 3 + depth * 2);
        for (var i = depth - 1; i >= 0; i--)
        {
            var cb = (col >> i) & 1;
            var rb = (row >> i) & 1;
            sb.Append('.').Append(BitsToQuadrant(cb, rb));
        }
        return sb.ToString();
    }

    /// <summary>从编码解析到网格坐标 — 象限序逐层累积 col/row 位</summary>
    internal static (int Col, int Row) DecodeToGrid(string code)
    {
        var span = code.AsSpan();
        var col = 0;
        var row = 0;
        for (var i = 2; i < span.Length; i += 2)
        {
            var q = span[i + 1] - '0';
            var (cb, rb) = QuadrantToBits(q);
            col = (col << 1) | cb;
            row = (row << 1) | rb;
        }
        return (col, row);
    }

    /// <summary>象限序 → (col 位, row 位)：SE/NE 右=1，SW/SE 下=1</summary>
    private static (int ColBit, int RowBit) QuadrantToBits(int q) => q switch
    {
        0 => (0, 1),
        1 => (1, 1),
        2 => (0, 0),
        3 => (1, 0),
        _ => (0, 0)
    };

    /// <summary>(col 位, row 位) → 象限序</summary>
    private static int BitsToQuadrant(int col, int row) => (col, row) switch
    {
        (0, 1) => 0,
        (1, 1) => 1,
        (0, 0) => 2,
        (1, 0) => 3,
        _ => 2
    };

    /// <summary>八方位 → 网格增量（N=row 减，图片 y 轴向下）</summary>
    private static (int DCol, int DRow) DirectionToDelta(CardinalDirection d) => d switch
    {
        CardinalDirection.N => (0, -1),
        CardinalDirection.S => (0, 1),
        CardinalDirection.W => (-1, 0),
        CardinalDirection.E => (1, 0),
        CardinalDirection.NW => (-1, -1),
        CardinalDirection.NE => (1, -1),
        CardinalDirection.SW => (-1, 1),
        CardinalDirection.SE => (1, 1),
        _ => (0, 0)
    };

    /// <summary>从编码取最后象限（根 L0 返回 SW 默认）</summary>
    private static Quadrant GetQuadrantFromCode(string code)
    {
        if (code.Length <= 2) return Quadrant.SW;
        return (code[^1] - '0') switch
        {
            0 => Quadrant.SW,
            1 => Quadrant.SE,
            2 => Quadrant.NW,
            3 => Quadrant.NE,
            _ => Quadrant.SW
        };
    }

    /// <summary>计算格子相对图片中心的方位（中文，如"左上"）— 辅助 LLM 方位感知</summary>
    private static string ComputeRegion(int x, int y, int w, int h, int imageWidth, int imageHeight)
    {
        var cx = x + w / 2.0;
        var cy = y + h / 2.0;
        var mx = imageWidth / 2.0;
        var my = imageHeight / 2.0;
        var isLeft = cx < mx;
        var isRight = cx > mx;
        var isTop = cy < my;
        var isBottom = cy > my;

        if (!isLeft && !isRight && !isTop && !isBottom) return "居中";
        var horizontal = isLeft ? "左" : (isRight ? "右" : "");
        var vertical = isTop ? "上" : (isBottom ? "下" : "");
        return horizontal + vertical;
    }
}
