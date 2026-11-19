namespace Tools.Handlers;

/// <summary>
/// 桌面情景模式 zoom 工具处理器 — 选象限缩小，看不清继续 zoom，看清推荐 detect
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public sealed class DesktopSceneZoomToolHandlers {
    private readonly IDesktopSceneZoomService _zoomService;

    /// <summary>
    /// 初始化 zoom 工具处理器
    /// </summary>
    /// <param name="zoomService">桌面场景缩放服务</param>
    public DesktopSceneZoomToolHandlers(IDesktopSceneZoomService zoomService) {
        _zoomService = zoomService ?? throw new ArgumentNullException(nameof(zoomService));
    }

    /// <summary>
    /// 选 1/2/3/4 象限缩小 — 看不清就反复 zoom，看清后调 desktop_detect 识别 UI 元素
    /// </summary>
    /// <param name="sceneId">场景 ID</param>
    /// <param name="quadrant">象限编号: 1=左上 2=右上 3=左下 4=右下</param>
    /// <param name="back">是否退回上一层（纠偏用），默认 false</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果，含子图 + 清晰度 + suggested_next(推荐 zoom 或 detect)</returns>
    [McpTool("desktop_zoom", "选 1/2/3/4 象限缩小，返回更清晰的子图。看不清就反复 zoom，看清后调 detect。back=true 退回上一层纠偏。", "desktop")]
    public async Task<ToolResult> ZoomAsync(
        [McpToolParameter("场景 ID", Required = true)] string sceneId,
        [McpToolParameter("象限编号: 1=左上 2=右上 3=左下 4=右下", Required = true)] int quadrant,
        [McpToolParameter("是否退回上一层（纠偏用），默认 false", Required = false)] bool back = false,
        CancellationToken cancellationToken = default) {
        var zoom = await _zoomService.ZoomAsync(sceneId, quadrant, back, cancellationToken).ConfigureAwait(false);

        var suggestedNext = zoom.IsClearEnough
            ? new DesktopSuggestedNext { Tool = "desktop_detect", Reason = "已缩放到可识别粒度，调 detect 识别 UI 元素" }
            : new DesktopSuggestedNext { Tool = "desktop_zoom", Reason = "仍看不清，继续选象限缩小" };

        var result = new DesktopZoomResult {
            SceneId = sceneId,
            SubImage = zoom.SubImageBase64,
            CurrentCell = zoom.CurrentCellCode,
            CurrentDepth = zoom.CurrentDepth,
            RegionWidth = zoom.RegionWidth,
            RegionHeight = zoom.RegionHeight,
            IsClearEnough = zoom.IsClearEnough,
            SuggestedNext = [suggestedNext]
        };
        return ToolResultBuilder.Success()
            .WithText(JsonSerializer.Serialize(result, DesktopSceneToolJsonContext.Default.DesktopZoomResult))
            .Build();
    }
}
