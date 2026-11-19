namespace Tools.Handlers;

/// <summary>
/// 桌面情景模式 detect 工具处理器 — 多模态识别 UI 元素，按钮推荐 click，输入框推荐 type
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public sealed class DesktopSceneDetectToolHandlers {
    private readonly IDesktopSceneDetectService _detectService;

    /// <summary>
    /// 初始化 detect 工具处理器
    /// </summary>
    /// <param name="detectService">桌面场景检测服务</param>
    public DesktopSceneDetectToolHandlers(IDesktopSceneDetectService detectService) {
        _detectService = detectService ?? throw new ArgumentNullException(nameof(detectService));
    }

    /// <summary>
    /// 多模态识别当前区域的 UI 元素 — 识别到按钮推荐 click，输入框推荐 type
    /// </summary>
    /// <param name="sceneId">场景 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>工具执行结果，含元素列表 + suggested_next(推荐 click 或 type)</returns>
    [McpTool("desktop_detect", "多模态识别当前区域的 UI 元素，返回按钮/输入框列表。识别到按钮推荐 click，输入框推荐 type。", "desktop")]
    public async Task<ToolResult> DetectAsync(
        [McpToolParameter("场景 ID", Required = true)] string sceneId,
        CancellationToken cancellationToken = default) {
        var detection = await _detectService.DetectAsync(sceneId, cancellationToken).ConfigureAwait(false);

        var elements = detection.Elements
            .Select(e => new DesktopDetectedElementDto { Type = e.Type, Label = e.Label, Cell = e.CellCode })
            .ToList();

        var suggestions = new List<DesktopSuggestedNext>();
        foreach (var e in detection.Elements) {
            if (e.Type == "button")
                suggestions.Add(new DesktopSuggestedNext { Tool = "desktop_click", Reason = "识别到按钮，点击" });
            if (e.Type == "input")
                suggestions.Add(new DesktopSuggestedNext { Tool = "desktop_type", Reason = "识别到输入框，输入文字" });
        }

        var result = new DesktopDetectResult {
            SceneId = sceneId,
            Elements = elements,
            SuggestedNext = suggestions
        };
        return ToolResultBuilder.Success()
            .WithText(JsonSerializer.Serialize(result, DesktopSceneToolJsonContext.Default.DesktopDetectResult))
            .Build();
    }
}
