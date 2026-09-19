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

        var nextTool = zoom.IsClearEnough ? "desktop_detect" : "desktop_zoom";
        var nextReason = zoom.IsClearEnough
            ? "已缩放到可识别粒度，调 detect 识别 UI 元素"
            : "仍看不清，继续选象限缩小";
        var clearFlag = zoom.IsClearEnough ? "true" : "false";

        var json = $$"""
            {
              "scene_id": "{{sceneId}}",
              "sub_image": "{{zoom.SubImageBase64}}",
              "current_cell": "{{zoom.CurrentCellCode}}",
              "current_depth": {{zoom.CurrentDepth}},
              "region_width": {{zoom.RegionWidth}},
              "region_height": {{zoom.RegionHeight}},
              "is_clear_enough": {{clearFlag}},
              "suggested_next": [{
                "tool": "{{nextTool}}",
                "reason": "{{nextReason}}"
              }]
            }
            """;
        return ToolResultBuilder.Success().WithText(json).Build();
    }
}