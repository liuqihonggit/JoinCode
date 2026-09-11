namespace JoinCode.Vision.ToolHandlers;

/// <summary>
/// 四叉树桌面叠加工具处理器(ADR 0032 延伸应用)— 把四叉树格子转换为屏幕坐标供 LLM 调 show_desktop_overlay 画框
/// </summary>
[McpToolDispatch(ToolCategory.Vision)]
public class QuadtreeDesktopOverlayToolHandlers
{
    private readonly IQuadtreeAnnotator _annotator;
    private readonly IQuadtreeDesktopOverlayMapper _mapper;
    private readonly ILogger<QuadtreeDesktopOverlayToolHandlers>? _logger;

    public QuadtreeDesktopOverlayToolHandlers(
        IQuadtreeAnnotator annotator,
        IQuadtreeDesktopOverlayMapper mapper,
        ILogger<QuadtreeDesktopOverlayToolHandlers>? logger = null)
    {
        _annotator = annotator ?? throw new ArgumentNullException(nameof(annotator));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _logger = logger;
    }

    /// <summary>把四叉树格子转换为屏幕绝对坐标矩形列表 — 返回 JSON 数组,供 LLM 调 show_desktop_overlay 在桌面画框</summary>
    [McpTool("quadtree_to_screen_rects", "把四叉树格子转换为屏幕绝对坐标矩形列表(ADR 0032延伸)。返回JSON数组,每项含cellCode/screenX/screenY/width/height/alpha。供LLM逐个调show_desktop_overlay在桌面实际画框。不传paintsJson时全部格子可见(alpha=0.3)", "vision")]
    public Task<ToolResult> QuadtreeToScreenRectsAsync(
        [McpToolParameter("原图宽度(像素)", Required = true)] int imageWidth,
        [McpToolParameter("原图高度(像素)", Required = true)] int imageHeight,
        [McpToolParameter("四叉树层数", Required = true)] int depth,
        [McpToolParameter("原图左上角在屏幕的X坐标", Required = true)] int originScreenX,
        [McpToolParameter("原图左上角在屏幕的Y坐标", Required = true)] int originScreenY,
        [McpToolParameter("染色映射JSON(可选),不传则全部格子可见。格式: {\"L0.0\":0.5}", Required = false)] string? paintsJson = null,
        CancellationToken ct = default)
    {
        if (imageWidth <= 0 || imageHeight <= 0)
            return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS160] 图片尺寸必须为正").Build());
        if (depth < 0)
            return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS162] depth 不能为负").Build());

        var grid = _annotator.BuildGrid(imageWidth, imageHeight, depth);

        if (!string.IsNullOrWhiteSpace(paintsJson))
        {
            Dictionary<string, double>? paints;
            try
            {
                paints = RelaxedJsonSerializer.Deserialize(paintsJson, VisionJsonContext.Default.DictionaryStringDouble);
            }
            catch (JsonException)
            {
                return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS161] paintsJson 解析失败").Build());
            }
            if (paints is null || paints.Count == 0)
                return Task.FromResult(ToolResultBuilder.Error().WithText("[VIS161] paintsJson 解析失败或为空").Build());
            grid = _annotator.PaintCells(grid, paints);
        }
        else
        {
            var defaultPaints = new Dictionary<string, double>(grid.Cells.Count);
            foreach (var cell in grid.Cells)
                defaultPaints[cell.Code] = 0.3;
            grid = _annotator.PaintCells(grid, defaultPaints);
        }

        var rects = _mapper.MapToScreen(grid, originScreenX, originScreenY);
        var json = JsonSerializer.Serialize(rects, VisionJsonContext.Default.ListQuadtreeDesktopRect);
        _logger?.LogInformation("四叉树屏幕坐标转换: {Count} 个可见格子, 原点({X},{Y})", rects.Count, originScreenX, originScreenY);
        return Task.FromResult(ToolResultBuilder.Success().WithText(json).Build());
    }
}
