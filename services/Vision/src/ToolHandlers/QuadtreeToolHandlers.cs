namespace JoinCode.Vision.ToolHandlers;

/// <summary>
/// 四叉树标注工具处理器（M1）— 多模态隐喻显露工具的网格标注模块
/// 提供 6 个 MCP 工具：quadtree_build/zoom/paint/render/neighbor + screen_indicate
/// 编码：数字点分路径 L0.2.1，象限序 SW=0/SE=1/NW=2/NE=3（左下起算）
/// </summary>
[McpToolDispatch(ToolCategory.Vision)]
public class QuadtreeToolHandlers
{
    private readonly IQuadtreeAnnotator _annotator;
    private readonly IQuadtreeRenderer _renderer;
    private readonly ILogger<QuadtreeToolHandlers>? _logger;

    public QuadtreeToolHandlers(
        IQuadtreeAnnotator annotator,
        IQuadtreeRenderer renderer,
        ILogger<QuadtreeToolHandlers>? logger = null)
    {
        _annotator = annotator ?? throw new ArgumentNullException(nameof(annotator));
        _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
        _logger = logger;
    }

    /// <summary>构建四叉树网格 — 返回所有格子的编码/坐标/方位，供 LLM 规划标注策略</summary>
    [McpTool("quadtree_build", "在图片上构建四叉树网格，返回所有格子的编码(L0.2.1格式)/坐标/方位/象限。depth=1为4格，2为16格，3为64格", "vision")]
    public Task<ToolResult> QuadtreeBuildAsync(
        [McpToolParameter("图片 base64 PNG/JPG 编码", Required = true)] string imageBase64,
        [McpToolParameter("四叉树层数（1=4格, 2=16格, 3=64格），默认2", Required = false)] int depth = 2,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS100] imageBase64 不能为空").Build());
        if (depth < 0)
            return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS101] depth 不能为负").Build());

        if (!TryGetImageDimensions(imageBase64, out var width, out var height, out var dimError))
            return Task.FromResult(ToolResultBuilder.Error().WithText($"[VIS102] {dimError}").Build());

        var grid = _annotator.BuildGrid(width, height, depth);
        var text = FormatGrid(grid, "四叉树网格构建完成");
        return Task.FromResult(ToolResultBuilder.Success().WithText(text).Build());
    }

    /// <summary>聚焦格子 — 裁剪子图并重新构建四叉树编码，用于递归深挖细节</summary>
    [McpTool("quadtree_zoom", "聚焦指定格子，裁剪子图并重新构建四叉树网格。返回子图base64+新网格（编码重置L0起算）", "vision")]
    public async Task<ToolResult> QuadtreeZoomAsync(
        [McpToolParameter("原图 base64", Required = true)] string imageBase64,
        [McpToolParameter("要聚焦的格子编码（如 L0.2.1）", Required = true)] string cellCode,
        [McpToolParameter("源格子所在的四叉树层数", Required = true)] int sourceDepth,
        [McpToolParameter("子图新网格层数，默认2", Required = false)] int targetDepth = 2,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return ToolResultBuilder.Error().WithText("[VIS110] imageBase64 不能为空").Build();
        if (string.IsNullOrWhiteSpace(cellCode))
            return ToolResultBuilder.Error().WithText("[VIS111] cellCode 不能为空").Build();

        if (!TryGetImageDimensions(imageBase64, out var width, out var height, out var dimError))
            return ToolResultBuilder.Error().WithText($"[VIS113] {dimError}").Build();

        QuadtreeZoomResult zoomResult;
        try
        {
            zoomResult = await _renderer.ZoomAsync(imageBase64, cellCode, width, height, sourceDepth, targetDepth, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex) when (ex.Message.StartsWith("[VIS112]", StringComparison.Ordinal) || ex.Message.StartsWith("[VIS013]", StringComparison.Ordinal))
        {
            return ToolResultBuilder.Error().WithText(ex.Message).Build();
        }
        var text = FormatGrid(zoomResult.Grid, $"聚焦格子 {cellCode} → 子图 {zoomResult.Grid.ImageWidth}x{zoomResult.Grid.ImageHeight}");

        return ToolResultBuilder.Success()
            .WithText(text)
            .WithImage(zoomResult.SubImageBase64, "image/png")
            .Build();
    }

    /// <summary>批量染色格子 — 更新 alpha 值，返回更新后的网格状态</summary>
    [McpTool("quadtree_paint", "批量染色格子（设置alpha强度），返回更新后的网格。paintsJson格式: {\"L0.0\":0.5,\"L0.1\":0.8}，alpha范围0..1", "vision")]
    public Task<ToolResult> QuadtreePaintAsync(
        [McpToolParameter("原图宽度（像素）", Required = true)] int imageWidth,
        [McpToolParameter("原图高度（像素）", Required = true)] int imageHeight,
        [McpToolParameter("四叉树层数", Required = true)] int depth,
        [McpToolParameter("染色映射JSON: {\"格子编码\":alpha}，alpha范围0..1", Required = true)] string paintsJson,
        CancellationToken ct = default)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS120] 图片尺寸必须为正").Build());
        if (string.IsNullOrWhiteSpace(paintsJson))
            return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS121] paintsJson 不能为空").Build());

        Dictionary<string, double>? paints;
        try
        {
            paints = JsonSerializer.Deserialize(paintsJson, VisionJsonContext.Default.DictionaryStringDouble);
        }
        catch (JsonException)
        {
            return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS122] paintsJson 解析失败或为空").Build());
        }
        if (paints is null || paints.Count == 0)
            return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS122] paintsJson 解析失败或为空").Build());

        var grid = _annotator.BuildGrid(imageWidth, imageHeight, depth);
        var paintedGrid = _annotator.PaintCells(grid, paints);
        var text = FormatGrid(paintedGrid, "染色完成");
        return Task.FromResult(ToolResultBuilder.Success().WithText(text).Build());
    }

    /// <summary>渲染虚线网格叠加到原图 — 返回标注后的图片 base64</summary>
    [McpTool("quadtree_render", "渲染虚线网格叠加到原图，返回标注图片base64。不传paintsJson时显示全部网格线(alpha=0.3)", "vision")]
    public async Task<ToolResult> QuadtreeRenderAsync(
        [McpToolParameter("原图 base64", Required = true)] string imageBase64,
        [McpToolParameter("原图宽度（像素）", Required = true)] int imageWidth,
        [McpToolParameter("原图高度（像素）", Required = true)] int imageHeight,
        [McpToolParameter("四叉树层数", Required = true)] int depth,
        [McpToolParameter("染色映射JSON（可选），不传则显示全部网格线", Required = false)] string? paintsJson = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return ToolResultBuilder.Error().WithText("[VIS130] imageBase64 不能为空").Build();
        if (imageWidth <= 0 || imageHeight <= 0)
            return ToolResultBuilder.Error().WithText("[VIS131] 图片尺寸必须为正").Build();

        var grid = _annotator.BuildGrid(imageWidth, imageHeight, depth);

        if (!string.IsNullOrWhiteSpace(paintsJson))
        {
            Dictionary<string, double>? paints;
            try
            {
                paints = JsonSerializer.Deserialize(paintsJson, VisionJsonContext.Default.DictionaryStringDouble);
            }
            catch (JsonException)
            {
                return ToolResultBuilder.Error().WithText("[VIS132] paintsJson 解析失败").Build();
            }
            if (paints is not null && paints.Count > 0)
                grid = _annotator.PaintCells(grid, paints);
        }
        else
        {
            var defaultPaints = new Dictionary<string, double>(grid.Cells.Count);
            foreach (var cell in grid.Cells)
                defaultPaints[cell.Code] = 0.3;
            grid = _annotator.PaintCells(grid, defaultPaints);
        }

        QuadtreeRenderResult renderResult;
        try
        {
            renderResult = await _renderer.RenderAsync(imageBase64, grid, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex) when (ex.Message.StartsWith("[VIS020]", StringComparison.Ordinal))
        {
            return ToolResultBuilder.Error().WithText(ex.Message).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"渲染完成: {imageWidth}x{imageHeight} depth={depth} 格子数={grid.Cells.Count}")
            .WithImage(renderResult.RenderedBase64, renderResult.MediaType)
            .Build();
    }

    /// <summary>八方位邻居查询 — 返回相邻格子编码，辅助 LLM 方位导航</summary>
    [McpTool("quadtree_neighbor", "查询格子的八方位邻居编码（同层）。方向: N/S/W/E/NW/NE/SW/SE，边界外返回null", "vision")]
    public Task<ToolResult> QuadtreeNeighborAsync(
        [McpToolParameter("源格子编码（如 L0.2.1）", Required = true)] string cellCode,
        [McpToolParameter("方位方向", Required = true, EnumValues = new[] { "N", "S", "W", "E", "NW", "NE", "SW", "SE" })] string direction,
        [McpToolParameter("原图宽度（像素）", Required = true)] int imageWidth,
        [McpToolParameter("原图高度（像素）", Required = true)] int imageHeight,
        [McpToolParameter("四叉树层数", Required = true)] int depth,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(cellCode))
            return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS140] cellCode 不能为空").Build());

        var dir = CardinalDirectionExtensions.FromValue(direction);
        if (dir is null)
            return Task.FromResult(ToolResultBuilder.Error().WithText($"[VIS141] 无效方向: {direction}，可选: N/S/W/E/NW/NE/SW/SE").Build());

        var neighbor = _annotator.GetNeighbor(cellCode, dir.Value, imageWidth, imageHeight, depth);
        var text = neighbor is null
            ? $"格子 {cellCode} 的 {direction} 方向邻居: 越界（不存在）"
            : $"格子 {cellCode} 的 {direction} 方向邻居: {neighbor}";

        return Task.FromResult(ToolResultBuilder.Success().WithText(text).Build());
    }

    /// <summary>高亮当前观察区域 — 在图片上标注指定格子并返回标注图片base64（不修改桌面）</summary>
    [McpTool("screen_indicate", "在图片上标注指定格子，返回标注后的图片base64。注意:此工具只在图片上画框返回,不在桌面上实际高亮。如需桌面实际高亮请用show_desktop_overlay。前置:需先screenshot获取imageBase64+quadtree_build获取cellCode", "vision")]
    public async Task<ToolResult> ScreenIndicateAsync(
        [McpToolParameter("原图 base64", Required = true)] string imageBase64,
        [McpToolParameter("要高亮的格子编码（如 L0.2.1）", Required = true)] string cellCode,
        [McpToolParameter("原图宽度（像素）", Required = true)] int imageWidth,
        [McpToolParameter("原图高度（像素）", Required = true)] int imageHeight,
        [McpToolParameter("四叉树层数", Required = true)] int depth,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(imageBase64))
            return ToolResultBuilder.Error().WithText("[VIS150] imageBase64 不能为空").Build();
        if (string.IsNullOrWhiteSpace(cellCode))
            return ToolResultBuilder.Error().WithText("[VIS151] cellCode 不能为空").Build();

        var grid = _annotator.BuildGrid(imageWidth, imageHeight, depth);
        var paints = new Dictionary<string, double> { [cellCode] = 1.0 };
        var paintedGrid = _annotator.PaintCells(grid, paints);

        QuadtreeRenderResult renderResult;
        try
        {
            renderResult = await _renderer.RenderAsync(imageBase64, paintedGrid, ct).ConfigureAwait(false);
        }
        catch (ArgumentException ex) when (ex.Message.StartsWith("[VIS020]", StringComparison.Ordinal))
        {
            return ToolResultBuilder.Error().WithText(ex.Message).Build();
        }

        return ToolResultBuilder.Success()
            .WithText($"高亮区域: {cellCode} (depth={depth})")
            .WithImage(renderResult.RenderedBase64, renderResult.MediaType)
            .Build();
    }

    /// <summary>从 base64 解码图片获取尺寸 — 容错版，失败返回 false + 错误描述</summary>
    private static bool TryGetImageDimensions(string imageBase64, out int width, out int height, out string error)
    {
        if (!VisionBase64.TryDecode(imageBase64, out var bytes, out error))
        {
            width = 0;
            height = 0;
            return false;
        }
        using var bitmap = SKBitmap.Decode(bytes);
        if (bitmap is null)
        {
            width = 0;
            height = 0;
            error = "无法解码图片，请检查 base64 编码";
            return false;
        }
        width = bitmap.Width;
        height = bitmap.Height;
        error = string.Empty;
        return true;
    }

    /// <summary>格式化网格为可读文本 — 供 LLM 理解格子布局</summary>
    private static string FormatGrid(QuadtreeGrid grid, string title)
    {
        var size = 1 << grid.Depth;
        var sb = new StringBuilder(256 + grid.Cells.Count * 80);
        sb.AppendLine(title);
        sb.AppendLine($"图片尺寸: {grid.ImageWidth}x{grid.ImageHeight}");
        sb.AppendLine($"四叉树层数: {grid.Depth} ({size}x{size} = {grid.Cells.Count} 格)");
        sb.AppendLine();
        sb.AppendLine("格子列表:");
        foreach (var cell in grid.Cells)
        {
            var alphaStr = cell.Alpha <= -1 ? "-" : cell.Alpha.ToString("F2", CultureInfo.InvariantCulture);
            sb.Append("  ").Append(cell.Code).Append(" [").Append(cell.Quadrant.ToValue()).Append("] ");
            sb.Append(cell.Region is not null ? cell.Region : "").Append(' ', 4);
            sb.Append(" (").Append(cell.X).Append(',').Append(cell.Y).Append(") ");
            sb.Append(cell.Width).Append('x').Append(cell.Height);
            sb.Append(" alpha=").AppendLine(alphaStr);
        }
        return sb.ToString();
    }
}
