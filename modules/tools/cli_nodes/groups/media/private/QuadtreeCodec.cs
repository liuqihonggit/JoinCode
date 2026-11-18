// quadtree 网格纯算法 — 从 modules/perception/vision/quadtree/QuadtreeEncoder.cs 移植。
// 只保留纯 C# 网格计算（BuildGrid/GetNeighbor/PaintCells/坐标映射），剔除 [Register] DI 特性与
// SkiaSharp 图像解码。quadtree_build 唯一的 SKBitmap.Decode（取图片宽高）改用纯 BCL PngHeaderParser。
// 编码：数字点分路径 L0.2.1，象限序 SW=0/SE=1/NW=2/NE=3（左下起算）。




namespace JoinCode.CliNode.Media;

internal enum Quadrant { SW, SE, NW, NE }

internal enum CardinalDirection { N, S, W, E, NW, NE, SW, SE }

internal sealed record QuadtreeCell(
    string Code, Quadrant Quadrant, string Region,
    int X, int Y, int Width, int Height, double Alpha);

internal sealed record QuadtreeGrid(
    IReadOnlyList<QuadtreeCell> Cells, int ImageWidth, int ImageHeight, int Depth);

internal sealed record QuadtreeDesktopRect(
    [property: JsonPropertyName("cellCode")] string CellCode,
    [property: JsonPropertyName("screenX")] int ScreenX,
    [property: JsonPropertyName("screenY")] int ScreenY,
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("alpha")] double Alpha);

/// <summary>四叉树编码器 — 纯网格计算，无图像/LLM/DI 依赖。</summary>
internal static class QuadtreeCodec {
    public static QuadtreeGrid BuildGrid(int imageWidth, int imageHeight, int depth) {
        if (imageWidth <= 0) throw new ArgumentException("图片宽度必须为正（--image_base64 无法解析出有效宽度）");
        if (imageHeight <= 0) throw new ArgumentException("图片高度必须为正");
        if (depth < 0) throw new ArgumentException("层数不能为负（--depth >= 0）");

        var size = 1 << depth;
        var cellW = imageWidth / size;
        var cellH = imageHeight / size;
        var cells = new List<QuadtreeCell>(size * size);
        for (var row = 0; row < size; row++) {
            for (var col = 0; col < size; col++) {
                var x = col * cellW;
                var y = row * cellH;
                var code = EncodeFromGrid(col, row, depth);
                cells.Add(new QuadtreeCell(code, GetQuadrantFromCode(code), ComputeRegion(x, y, cellW, cellH, imageWidth, imageHeight),
                    x, y, cellW, cellH, -1));
            }
        }
        return new QuadtreeGrid(cells, imageWidth, imageHeight, depth);
    }

    public static string? GetNeighbor(string cellCode, CardinalDirection direction, int imageWidth, int imageHeight, int depth) {
        if (string.IsNullOrWhiteSpace(cellCode)) throw new ArgumentException("cellCode 不能为空");
        if (depth < 0) throw new ArgumentException("层数不能为负");
        var (col, row) = DecodeToGrid(cellCode);
        var size = 1 << depth;
        var (dcol, drow) = DirectionToDelta(direction);
        var ncol = col + dcol;
        var nrow = row + drow;
        if (ncol < 0 || nrow < 0 || ncol >= size || nrow >= size) return null;
        return EncodeFromGrid(ncol, nrow, depth);
    }

    public static QuadtreeGrid PaintCells(QuadtreeGrid grid, IReadOnlyDictionary<string, double> paints) {
        if (paints.Count == 0) return grid;
        var cells = new List<QuadtreeCell>(grid.Cells.Count);
        foreach (var cell in grid.Cells)
            cells.Add(paints.TryGetValue(cell.Code, out var alpha) ? cell with { Alpha = alpha } : cell);
        return new QuadtreeGrid(cells, grid.ImageWidth, grid.ImageHeight, grid.Depth);
    }

    public static IReadOnlyList<QuadtreeDesktopRect> MapToScreen(QuadtreeGrid grid, int ox, int oy) {
        var result = new List<QuadtreeDesktopRect>(grid.Cells.Count);
        foreach (var cell in grid.Cells) {
            if (cell.Alpha < 0) continue;
            result.Add(new QuadtreeDesktopRect(cell.Code, ox + cell.X, oy + cell.Y, cell.Width, cell.Height, cell.Alpha));
        }
        return result;
    }

    public static CardinalDirection? ParseDirection(string value) => value?.ToUpperInvariant() switch {
        "N" => CardinalDirection.N, "S" => CardinalDirection.S, "W" => CardinalDirection.W, "E" => CardinalDirection.E,
        "NW" => CardinalDirection.NW, "NE" => CardinalDirection.NE, "SW" => CardinalDirection.SW, "SE" => CardinalDirection.SE,
        _ => null
    };

    public static string QuadrantText(Quadrant q) => q switch {
        Quadrant.SW => "SW", Quadrant.SE => "SE", Quadrant.NW => "NW", Quadrant.NE => "NE", _ => "SW"
    };

    public static string FormatGrid(QuadtreeGrid grid, string title) {
        var size = 1 << grid.Depth;
        var sb = new StringBuilder(256 + grid.Cells.Count * 80);
        sb.AppendLine(title);
        sb.AppendLine($"图片尺寸: {grid.ImageWidth}x{grid.ImageHeight}");
        sb.AppendLine($"四叉树层数: {grid.Depth} ({size}x{size} = {grid.Cells.Count} 格)");
        sb.AppendLine();
        sb.AppendLine("格子列表:");
        foreach (var cell in grid.Cells) {
            var alpha = cell.Alpha <= -1 ? "-" : cell.Alpha.ToString("F2", CultureInfo.InvariantCulture);
            sb.Append("  ").Append(cell.Code).Append(" [").Append(QuadrantText(cell.Quadrant)).Append("] ")
              .Append(cell.Region).Append("    (").Append(cell.X).Append(',').Append(cell.Y).Append(") ")
              .Append(cell.Width).Append('x').Append(cell.Height).Append(" alpha=").AppendLine(alpha);
        }
        return sb.ToString();
    }

    /// <summary>从 base64 PNG 读宽高 — 纯 BCL，替代引擎的 SKBitmap.Decode（读 IHDR 16-23 字节）。</summary>
    public static (int width, int height) PngDimensions(string base64) {
        byte[] bytes;
        try {
            bytes = Convert.FromBase64String(base64);
        } catch (FormatException ex) {
            // 用户传错 base64 不该看到堆栈——转成 Rust 风格参数错误（原因+正确写法）。
            throw new ArgumentException($"imageBase64 不是合法 base64: {ex.Message}。正确写法: 传 screenshot 工具返回的 base64 PNG");
        }
        if (bytes.Length < 24 || bytes[0] != 0x89 || bytes[1] != 0x50)
            throw new ArgumentException("无效 PNG 数据：需传入 screenshot 返回的 base64 PNG");
        int ReadBE(int o) => (bytes[o] << 24) | (bytes[o + 1] << 16) | (bytes[o + 2] << 8) | bytes[o + 3];
        return (ReadBE(16), ReadBE(20));
    }

    private static string EncodeFromGrid(int col, int row, int depth) {
        if (depth == 0) return "L0";
        var sb = new StringBuilder("L0", 3 + depth * 2);
        for (var i = depth - 1; i >= 0; i--) {
            var cb = (col >> i) & 1;
            var rb = (row >> i) & 1;
            sb.Append('.').Append(BitsToQuadrant(cb, rb));
        }
        return sb.ToString();
    }

    private static (int Col, int Row) DecodeToGrid(string code) {
        var span = code.AsSpan();
        var col = 0; var row = 0;
        for (var i = 2; i < span.Length; i += 2) {
            var q = span[i + 1] - '0';
            var (cb, rb) = QuadrantToBits(q);
            col = (col << 1) | cb;
            row = (row << 1) | rb;
        }
        return (col, row);
    }

    private static (int, int) QuadrantToBits(int q) => q switch { 0 => (0, 1), 1 => (1, 1), 2 => (0, 0), 3 => (1, 0), _ => (0, 0) };
    private static int BitsToQuadrant(int col, int row) => (col, row) switch { (0, 1) => 0, (1, 1) => 1, (0, 0) => 2, (1, 0) => 3, _ => 2 };
    private static (int, int) DirectionToDelta(CardinalDirection d) => d switch {
        CardinalDirection.N => (0, -1), CardinalDirection.S => (0, 1), CardinalDirection.W => (-1, 0), CardinalDirection.E => (1, 0),
        CardinalDirection.NW => (-1, -1), CardinalDirection.NE => (1, -1), CardinalDirection.SW => (-1, 1), CardinalDirection.SE => (1, 1), _ => (0, 0)
    };
    private static Quadrant GetQuadrantFromCode(string code) {
        if (code.Length <= 2) return Quadrant.SW;
        return (code[^1] - '0') switch { 0 => Quadrant.SW, 1 => Quadrant.SE, 2 => Quadrant.NW, 3 => Quadrant.NE, _ => Quadrant.SW };
    }
    private static string ComputeRegion(int x, int y, int w, int h, int iw, int ih) {
        var cx = x + w / 2.0; var cy = y + h / 2.0;
        var mx = iw / 2.0; var my = ih / 2.0;
        var l = cx < mx; var r = cx > mx; var t = cy < my; var b = cy > my;
        if (!l && !r && !t && !b) return "居中";
        return (l ? "左" : r ? "右" : "") + (t ? "上" : b ? "下" : "");
    }
}
