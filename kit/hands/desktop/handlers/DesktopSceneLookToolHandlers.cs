namespace Tools.Handlers;

/// <summary>
/// 桌面情景模式 look 工具处理器 — 截图 + 四叉树网格标注，返回 suggested_next 引导 AI 调 zoom
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public sealed class DesktopSceneLookToolHandlers
{
    private readonly IDesktopSceneCaptureService _captureService;

    /// <summary>
    /// 初始化 look 工具处理器
    /// </summary>
    /// <param name="captureService">桌面场景截图编排服务</param>
    public DesktopSceneLookToolHandlers(IDesktopSceneCaptureService captureService)
    {
        _captureService = captureService ?? throw new ArgumentNullException(nameof(captureService));
    }

    /// <summary>
    /// 截图并构建四叉树网格 — 场景第一步，AI 看带网格标注的图后调 desktop_zoom 选目标所在象限
    /// </summary>
    /// <param name="sceneId">场景 ID，首次调用可留空自动创建</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果，含截图 + 网格 + suggested_next(推荐 desktop_zoom)</returns>
    [McpTool("desktop_look", "截图并构建四叉树网格，返回带网格标注的截图。场景第一步，AI 看图后调 desktop_zoom 选目标所在象限缩小。", "desktop")]
    public async Task<ToolResult> LookAsync(
        [McpToolParameter("场景 ID，首次调用可留空自动创建", Required = false)] string? sceneId = null,
        CancellationToken cancellationToken = default)
    {
        var newSceneId = string.IsNullOrEmpty(sceneId)
            ? $"sc_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}"
            : sceneId;

        var capture = await _captureService.CaptureWithGridAsync(newSceneId, 2, cancellationToken).ConfigureAwait(false);

        var json = $$"""
            {
              "scene_id": "{{newSceneId}}",
              "screenshot": "{{capture.RenderedBase64}}",
              "image_width": {{capture.ImageWidth}},
              "image_height": {{capture.ImageHeight}},
              "depth": {{capture.Depth}},
              "suggested_next": [{
                "tool": "desktop_zoom",
                "reason": "截图已建四叉树网格，选目标所在象限缩小",
                "params_hint": "quadrant: 1=左上 2=右上 3=左下 4=右下"
              }]
            }
            """;
        return ToolResultBuilder.Success().WithText(json).Build();
    }
}
