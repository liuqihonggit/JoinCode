namespace JoinCode.Hands.Desktop;

/// <summary>
/// 多模态 UI 元素检测器 — 截图 → 多模态 LLM → 结构化 UI 元素列表（PRD V-02/V-03/V-04）
/// 通过 IQueryService 调用支持 vision 的 LLM，解析 JSON 响应返回 UiElement
/// </summary>
[Register(typeof(IUiElementDetector), ServiceLifetime.Singleton)]
public sealed partial class MultimodalUiElementDetector : ServiceEntity, IUiElementDetector
{
    private readonly IQueryService _queryService;
    private readonly ILogger<MultimodalUiElementDetector>? _logger;

    private static readonly ChatOptions VisionChatOptions = new()
    {
        Temperature = 0.3f,
        MaxTokens = 8000
    };

    public MultimodalUiElementDetector(IQueryService queryService, ILogger<MultimodalUiElementDetector>? logger = null)
    {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _logger = logger;
    }

    /// <summary>
    /// 检测截图中的所有 UI 元素（V-02 + V-03）
    /// </summary>
    /// <param name="base64Png">base64 编码的 PNG 截图</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>UI 元素列表（含类型/坐标/状态/语义描述）</returns>
    public async Task<UiElementDetectionResult> DetectAsync(string base64Png, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Png);
        cancellationToken.ThrowIfCancellationRequested();

        var messages = new MessageList();
        messages.AddSystemMessage(DetectSystemPrompt);
        messages.Add(new ApiMessage(MessageRole.User, "请识别这张截图中所有的 UI 元素，以 JSON 格式返回。")
        {
            ContentBlocks = [new ToolContent { Type = ToolContentType.Image, Data = base64Png, MimeType = "image/png" }]
        });

        var responseList = await _queryService.GetApiMessageContentsAsync(messages, VisionChatOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        var responseText = responseList.FirstOrDefault()?.Content ?? string.Empty;

        _logger?.LogDebug("UI元素检测响应长度: {Length}", responseText.Length);
        return ParseDetectionResult(responseText);
    }

    /// <summary>
    /// 按语义描述查找元素（V-04）— 如"红色的停止按钮"
    /// </summary>
    /// <param name="base64Png">base64 PNG 截图</param>
    /// <param name="description">语义描述（自然语言）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配度最高的元素，未找到返回 null</returns>
    public async Task<UiElement?> FindByDescriptionAsync(string base64Png, string description, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Png);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        cancellationToken.ThrowIfCancellationRequested();

        var messages = new MessageList();
        messages.AddSystemMessage(FindSystemPrompt);
        messages.Add(new ApiMessage(MessageRole.User, $"在截图中查找符合以下描述的 UI 元素：{description}")
        {
            ContentBlocks = [new ToolContent { Type = ToolContentType.Image, Data = base64Png, MimeType = "image/png" }]
        });

        var responseList = await _queryService.GetApiMessageContentsAsync(messages, VisionChatOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        var responseText = responseList.FirstOrDefault()?.Content ?? string.Empty;

        _logger?.LogDebug("UI元素查找响应长度: {Length}", responseText.Length);
        return ParseFindResult(responseText);
    }

    /// <summary>
    /// 解析检测响应 JSON → UiElementDetectionResult
    /// </summary>
    internal static UiElementDetectionResult ParseDetectionResult(string responseText)
    {
        var json = ExtractJson(responseText);
        if (string.IsNullOrEmpty(json))
            return new UiElementDetectionResult([], 0, 0);

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var imageWidth = root.TryGetProperty("imageWidth", out var wProp) && wProp.TryGetInt32(out var w) ? w : 0;
            var imageHeight = root.TryGetProperty("imageHeight", out var hProp) && hProp.TryGetInt32(out var h) ? h : 0;

            if (!root.TryGetProperty("elements", out var elementsProp) || elementsProp.ValueKind != JsonValueKind.Array)
                return new UiElementDetectionResult([], imageWidth, imageHeight);

            var elements = new List<UiElement>();
            foreach (var el in elementsProp.EnumerateArray())
            {
                var element = ParseUiElement(el);
                if (element is not null)
                    elements.Add(element);
            }

            return new UiElementDetectionResult(elements, imageWidth, imageHeight);
        }
        catch (JsonException)
        {
            return new UiElementDetectionResult([], 0, 0);
        }
    }

    /// <summary>
    /// 解析查找响应 JSON → 单个 UiElement
    /// </summary>
    internal static UiElement? ParseFindResult(string responseText)
    {
        var json = ExtractJson(responseText);
        if (string.IsNullOrEmpty(json))
            return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("found", out var foundProp) && foundProp.ValueKind == JsonValueKind.False)
                return null;

            if (root.TryGetProperty("element", out var elProp) && elProp.ValueKind == JsonValueKind.Object)
                return ParseUiElement(elProp);

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 解析单个 UI 元素 JSON 对象 → UiElement record
    /// </summary>
    internal static UiElement? ParseUiElement(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return null;

        var type = ParseElementType(el.TryGetProperty("type", out var tProp) ? tProp.GetString() : null);
        var text = el.TryGetProperty("text", out var textProp) ? textProp.GetString() : null;
        var description = el.TryGetProperty("description", out var descProp) ? descProp.GetString() : null;
        var x = el.TryGetProperty("x", out var xProp) && xProp.TryGetInt32(out var xv) ? xv : 0;
        var y = el.TryGetProperty("y", out var yProp) && yProp.TryGetInt32(out var yv) ? yv : 0;
        var width = el.TryGetProperty("width", out var wProp) && wProp.TryGetInt32(out var wv) ? wv : 0;
        var height = el.TryGetProperty("height", out var hProp) && hProp.TryGetInt32(out var hv) ? hv : 0;
        var state = ParseElementState(el.TryGetProperty("state", out var sProp) ? sProp.GetString() : null);
        var confidence = el.TryGetProperty("confidence", out var cProp) && cProp.TryGetDouble(out var cv) ? cv : 0.5;

        return new UiElement(type, text, description, x, y, width, height, state, confidence);
    }

    /// <summary>
    /// 从 LLM 响应中提取 JSON — 处理 ```json ``` 代码块包裹和纯 JSON 两种情况
    /// </summary>
    internal static string ExtractJson(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
            return string.Empty;

        var trimmed = responseText.Trim();

        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                var inner = trimmed.AsSpan(firstNewline + 1);
                var endFence = inner.LastIndexOf("```");
                if (endFence >= 0)
                    inner = inner[..endFence];
                return inner.ToString().Trim();
            }
        }

        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
            return trimmed.Substring(start, end - start + 1);

        return trimmed;
    }

    /// <summary>
    /// 字符串 → UiElementType 枚举（容错映射，未知返回 Unknown）
    /// </summary>
    internal static UiElementType ParseElementType(string? type) => (type ?? string.Empty).ToLowerInvariant() switch
    {
        "button" or "btn" => UiElementType.Button,
        "textbox" or "text_box" or "input" or "textinput" => UiElementType.TextBox,
        "menu" => UiElementType.Menu,
        "menuitem" or "menu_item" => UiElementType.MenuItem,
        "dialog" => UiElementType.Dialog,
        "progressbar" or "progress_bar" or "progress" => UiElementType.ProgressBar,
        "checkbox" or "check_box" => UiElementType.CheckBox,
        "radiobutton" or "radio_button" or "radio" => UiElementType.RadioButton,
        "icon" => UiElementType.Icon,
        "text" or "label" => UiElementType.Text,
        "image" or "img" => UiElementType.Image,
        "link" or "hyperlink" or "a" => UiElementType.Link,
        "combobox" or "combo_box" or "dropdown" or "select" => UiElementType.ComboBox,
        "listitem" or "list_item" or "li" => UiElementType.ListItem,
        "titlebar" or "title_bar" => UiElementType.TitleBar,
        "scrollbar" or "scroll_bar" or "scroll" => UiElementType.ScrollBar,
        _ => UiElementType.Unknown
    };

    /// <summary>
    /// 字符串 → ElementState 枚举（容错映射，未知返回 Normal）
    /// </summary>
    internal static ElementState ParseElementState(string? state) => (state ?? string.Empty).ToLowerInvariant() switch
    {
        "normal" or "default" or "enabled" => ElementState.Normal,
        "disabled" or "disable" or "grayed" => ElementState.Disabled,
        "selected" or "checked" => ElementState.Selected,
        "hovered" or "hover" => ElementState.Hovered,
        "focused" or "focus" or "active" => ElementState.Focused,
        "hidden" or "invisible" or "none" => ElementState.Hidden,
        "pressed" or "press" or "down" => ElementState.Pressed,
        _ => ElementState.Normal
    };

    private const string DetectSystemPrompt = """
        你是一个 UI 元素识别专家。分析给定的屏幕截图，识别所有可见的 UI 元素。

        返回 JSON 格式（只返回 JSON，不要其他文字）：
        {
          "imageWidth": <截图宽度像素>,
          "imageHeight": <截图高度像素>,
          "elements": [
            {
              "type": "button|textbox|menu|menuitem|dialog|progressbar|checkbox|radiobutton|icon|text|image|link|combobox|listitem|titlebar|scrollbar|unknown",
              "text": "<元素上的文字，无则null>",
              "description": "<元素的语义描述>",
              "x": <左上角X坐标>,
              "y": <左上角Y坐标>,
              "width": <宽度>,
              "height": <高度>,
              "state": "normal|disabled|selected|hovered|focused|hidden|pressed",
              "confidence": <0.0到1.0的置信度>
            }
          ]
        }

        坐标基于像素，左上角为原点(0,0)。尽量识别所有可见元素。
        """;

    private const string FindSystemPrompt = """
        你是一个 UI 元素查找专家。在给定的截图中查找符合描述的 UI 元素。

        返回 JSON 格式（只返回 JSON，不要其他文字）：
        {
          "found": true,
          "element": {
            "type": "button|textbox|menu|menuitem|dialog|progressbar|checkbox|radiobutton|icon|text|image|link|combobox|listitem|titlebar|scrollbar|unknown",
            "text": "<元素上的文字>",
            "description": "<语义描述>",
            "x": <X坐标>,
            "y": <Y坐标>,
            "width": <宽度>,
            "height": <高度>,
            "state": "normal|disabled|selected|hovered|focused|hidden|pressed",
            "confidence": <0.0到1.0>
          }
        }

        如果找不到匹配元素，返回 {"found": false, "element": null}。
        坐标基于像素，左上角为原点(0,0)。
        """;
}
