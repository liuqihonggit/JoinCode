namespace Tools.Handlers;

/// <summary>
/// 视觉理解工具处理器 — UI 元素检测与查找，暴露为 MCP 工具（PRD V-02/V-03/V-04）
/// </summary>
[McpToolDispatch(ToolCategory.DesktopControl)]
public class VisionToolHandlers
{
    private readonly IUiElementDetector _detector;
    private readonly IScreenCaptureService _capture;
    private readonly ILogger<VisionToolHandlers>? _logger;

    public VisionToolHandlers(
        IUiElementDetector detector,
        IScreenCaptureService capture,
        ILogger<VisionToolHandlers>? logger = null)
    {
        _detector = detector;
        _capture = capture;
        _logger = logger;
    }

    /// <summary>检测截图中所有 UI 元素（V-02 + V-03）</summary>
    [McpTool("detect_ui_elements", "截取屏幕并识别所有UI元素（按钮/输入框/菜单等），返回类型/坐标/状态/语义描述", "desktop")]
    public async Task<ToolResult> DetectUiElementsAsync(
        [McpToolParameter("已有截图的 base64 PNG（不传则自动截全屏）", Required = false)] string? base64Screenshot = null,
        CancellationToken ct = default)
    {
        var base64 = base64Screenshot;
        if (string.IsNullOrWhiteSpace(base64))
        {
            base64 = await _capture.CaptureFullScreenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(base64))
                return ToolResultBuilder.Error().WithText("截图失败").Build();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        UiElementDetectionResult result;
        try
        {
            result = await _detector.DetectAsync(base64, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ToolResultBuilder.Error().WithText("LLM 调用超时（30s），请检查 API Key 配置和网络连接").Build();
        }

        if (result.ImageWidth == 0 && result.ImageHeight == 0 && result.Elements.Count == 0)
            return ToolResultBuilder.Error().WithText("LLM 未返回有效识别结果，请检查 API Key 配置和网络连接").Build();

        var sb = new StringBuilder(512);
        sb.AppendLine($"截图尺寸: {result.ImageWidth}x{result.ImageHeight}");
        sb.AppendLine($"检测到 {result.Elements.Count} 个 UI 元素:");
        for (var i = 0; i < result.Elements.Count; i++)
        {
            var el = result.Elements[i];
            sb.AppendLine($"  [{i + 1}] {el.Type}" +
                (el.Text is not null ? $" \"{el.Text}\"" : string.Empty) +
                $" @ ({el.X},{el.Y}) {el.Width}x{el.Height}" +
                $" [{el.State}] conf={el.Confidence:F2}");
            if (!string.IsNullOrEmpty(el.Description))
                sb.AppendLine($"        描述: {el.Description}");
        }

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }

    /// <summary>按语义描述查找 UI 元素（V-04）</summary>
    [McpTool("find_element", "按自然语言描述查找UI元素（如\"红色的停止按钮\"），返回坐标供 mouse_click 使用", "desktop")]
    public async Task<ToolResult> FindElementAsync(
        [McpToolParameter("元素的语义描述（自然语言）", Required = true)] string description,
        [McpToolParameter("已有截图的 base64 PNG（不传则自动截全屏）", Required = false)] string? base64Screenshot = null,
        CancellationToken ct = default)
    {
        var base64 = base64Screenshot;
        if (string.IsNullOrWhiteSpace(base64))
        {
            base64 = await _capture.CaptureFullScreenAsync(ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(base64))
                return ToolResultBuilder.Error().WithText("截图失败").Build();
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(30));

        UiElement? element;
        try
        {
            element = await _detector.FindByDescriptionAsync(base64, description, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return ToolResultBuilder.Error().WithText("LLM 调用超时（30s），请检查 API Key 配置和网络连接").Build();
        }

        if (element is null)
            return ToolResultBuilder.Success().WithText($"未找到符合描述「{description}」的 UI 元素").Build();

        var sb = new StringBuilder(256);
        sb.AppendLine($"找到元素: {element.Type}" +
            (element.Text is not null ? $" \"{element.Text}\"" : string.Empty) +
            $" @ ({element.X},{element.Y}) {element.Width}x{element.Height}" +
            $" [{element.State}] conf={element.Confidence:F2}");
        if (!string.IsNullOrEmpty(element.Description))
            sb.AppendLine($"描述: {element.Description}");
        sb.AppendLine($"中心坐标: ({element.X + element.Width / 2}, {element.Y + element.Height / 2})");
        sb.AppendLine($"可用于 mouse_click: x={element.X + element.Width / 2}, y={element.Y + element.Height / 2}");

        return ToolResultBuilder.Success().WithText(sb.ToString()).Build();
    }
}
