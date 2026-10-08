namespace JoinCode.Hands.Desktop;

/// <summary>
/// 多模态 UI 元素检测器 — 截图 → 多模态 LLM → 结构化 UI 元素列表（PRD V-02/V-03/V-04）
/// 通过 IQueryService 调用支持 vision 的 LLM，解析 JSON 响应返回 UiElement
/// </summary>
[Register(typeof(IUiElementDetector), ServiceLifetime.Singleton)]
public sealed partial class MultimodalUiElementDetector : ServiceEntity, IUiElementDetector {
    private readonly IQueryService _queryService;
    private readonly ILogger<MultimodalUiElementDetector>? _logger;

    private static readonly ChatOptions VisionChatOptions = new() {
        Temperature = 0.3f,
        MaxTokens = 8000
    };

    /// <summary>构造多模态 UI 元素检测器实例。</summary>
    /// <param name="queryService">LLM 查询服务，用于调用支持 vision 的模型识别 UI 元素。</param>
    /// <param name="logger">可选的日志记录器，传入 null 时静默运行。</param>
    public MultimodalUiElementDetector(IQueryService queryService, ILogger<MultimodalUiElementDetector>? logger = null) {
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _logger = logger;
    }

    /// <summary>
    /// 检测截图中的所有 UI 元素（V-02 + V-03）
    /// </summary>
    /// <param name="base64Png">base64 编码的 PNG 截图</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>UI 元素列表（含类型/坐标/状态/语义描述）</returns>
    public async Task<UiElementDetectionResult> DetectAsync(string base64Png, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Png);
        cancellationToken.ThrowIfCancellationRequested();

        var messages = new MessageList();
        messages.AddSystemMessage(DetectSystemPrompt);
        messages.Add(new ApiMessage(MessageRole.User, "请识别这张截图中所有的 UI 元素，以 JSON 格式返回。") {
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
    public async Task<UiElement?> FindByDescriptionAsync(string base64Png, string description, CancellationToken cancellationToken = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(base64Png);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        cancellationToken.ThrowIfCancellationRequested();

        var messages = new MessageList();
        messages.AddSystemMessage(FindSystemPrompt);
        messages.Add(new ApiMessage(MessageRole.User, $"在截图中查找符合以下描述的 UI 元素：{description}") {
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
    internal static UiElementDetectionResult ParseDetectionResult(string responseText) {
        var json = ExtractJson(responseText);
        if (string.IsNullOrEmpty(json))
            return new UiElementDetectionResult([], 0, 0);

        try {
            var dto = JsonSerializer.Deserialize(json, UiDetectionJsonContext.Default.UiDetectionResultDto);
            if (dto is null)
                return new UiElementDetectionResult([], 0, 0);

            var elements = (dto.Elements ?? []).Select(ToUiElement).ToList();
            return new UiElementDetectionResult(elements, dto.ImageWidth, dto.ImageHeight);
        } catch (JsonException) {
            return new UiElementDetectionResult([], 0, 0);
        }
    }

    /// <summary>
    /// 解析查找响应 JSON → 单个 UiElement
    /// </summary>
    internal static UiElement? ParseFindResult(string responseText) {
        var json = ExtractJson(responseText);
        if (string.IsNullOrEmpty(json))
            return null;

        try {
            var dto = JsonSerializer.Deserialize(json, UiDetectionJsonContext.Default.UiFindResultDto);
            if (dto is null)
                return null;

            if (dto.Found is false)
                return null;

            return dto.Element is null ? null : ToUiElement(dto.Element);
        } catch (JsonException) {
            return null;
        }
    }

    /// <summary>
    /// UiElementDto → UiElement record 转换（含枚举容错映射与 confidence 默认值补全）
    /// </summary>
    internal static UiElement ToUiElement(UiElementDto dto) {
        var type = ParseElementType(dto.Type);
        var state = ParseElementState(dto.State);
        var confidence = dto.Confidence ?? 0.5;
        return new UiElement(type, dto.Text, dto.Description, dto.X, dto.Y, dto.Width, dto.Height, state, confidence);
    }

    /// <summary>
    /// 从 LLM 响应中提取 JSON — 统一调用 LlmJsonHelper
    /// </summary>
    internal static string ExtractJson(string responseText)
        => LlmJsonHelper.ExtractJsonBlock(responseText) ?? LlmJsonHelper.ExtractInlineJson(responseText) ?? responseText.Trim();

    /// <summary>UI 元素类型字符串 → 枚举容错映射（忽略大小写）。</summary>
    private static readonly FrozenDictionary<string, UiElementType> ElementTypeMap = new Dictionary<string, UiElementType>(StringComparer.OrdinalIgnoreCase) {
        ["button"] = UiElementType.Button, ["btn"] = UiElementType.Button,
        ["textbox"] = UiElementType.TextBox, ["text_box"] = UiElementType.TextBox, ["input"] = UiElementType.TextBox, ["textinput"] = UiElementType.TextBox,
        ["menu"] = UiElementType.Menu,
        ["menuitem"] = UiElementType.MenuItem, ["menu_item"] = UiElementType.MenuItem,
        ["dialog"] = UiElementType.Dialog,
        ["progressbar"] = UiElementType.ProgressBar, ["progress_bar"] = UiElementType.ProgressBar, ["progress"] = UiElementType.ProgressBar,
        ["checkbox"] = UiElementType.CheckBox, ["check_box"] = UiElementType.CheckBox,
        ["radiobutton"] = UiElementType.RadioButton, ["radio_button"] = UiElementType.RadioButton, ["radio"] = UiElementType.RadioButton,
        ["icon"] = UiElementType.Icon,
        ["text"] = UiElementType.Text, ["label"] = UiElementType.Text,
        ["image"] = UiElementType.Image, ["img"] = UiElementType.Image,
        ["link"] = UiElementType.Link, ["hyperlink"] = UiElementType.Link, ["a"] = UiElementType.Link,
        ["combobox"] = UiElementType.ComboBox, ["combo_box"] = UiElementType.ComboBox, ["dropdown"] = UiElementType.ComboBox, ["select"] = UiElementType.ComboBox,
        ["listitem"] = UiElementType.ListItem, ["list_item"] = UiElementType.ListItem, ["li"] = UiElementType.ListItem,
        ["titlebar"] = UiElementType.TitleBar, ["title_bar"] = UiElementType.TitleBar,
        ["scrollbar"] = UiElementType.ScrollBar, ["scroll_bar"] = UiElementType.ScrollBar, ["scroll"] = UiElementType.ScrollBar,
    }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 字符串 → UiElementType 枚举（容错映射，未知返回 Unknown）
    /// </summary>
    internal static UiElementType ParseElementType(string? type)
        => ElementTypeMap.TryGetValue(type ?? string.Empty, out var t) ? t : UiElementType.Unknown;

    /// <summary>
    /// 字符串 → ElementState 枚举（容错映射，未知返回 Normal）
    /// </summary>
    internal static ElementState ParseElementState(string? state) => (state ?? string.Empty).ToLowerInvariant() switch {
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

/// <summary>
/// UI 元素检测响应 DTO — LLM 返回的 JSON 结构（检测模式，含 imageWidth/imageHeight/elements）
/// </summary>
public sealed class UiDetectionResultDto {
    /// <summary>截图宽度（像素）。</summary>
    [JsonPropertyName("imageWidth")]
    public int ImageWidth { get; set; }

    /// <summary>截图高度（像素）。</summary>
    [JsonPropertyName("imageHeight")]
    public int ImageHeight { get; set; }

    /// <summary>识别到的 UI 元素列表。</summary>
    [JsonPropertyName("elements")]
    public List<UiElementDto> Elements { get; set; } = [];
}

/// <summary>
/// UI 元素查找响应 DTO — LLM 返回的 JSON 结构（查找模式，含 found/element）
/// </summary>
public sealed class UiFindResultDto {
    /// <summary>是否找到匹配元素（null 表示 JSON 中未提供该字段，不视为"未找到"）。</summary>
    [JsonPropertyName("found")]
    public bool? Found { get; set; }

    /// <summary>匹配到的 UI 元素，未找到时为 null。</summary>
    [JsonPropertyName("element")]
    public UiElementDto? Element { get; set; }
}

/// <summary>
/// 单个 UI 元素 DTO — LLM 返回的元素 JSON 结构（type/text/description/x/y/width/height/state/confidence）
/// </summary>
public sealed class UiElementDto {
    /// <summary>元素类型字符串（button/textbox/menu 等，由 ParseElementType 映射为枚举）。</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>元素上的文字，无则 null。</summary>
    [JsonPropertyName("text")]
    public string? Text { get; set; }

    /// <summary>元素的语义描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>左上角 X 坐标（像素）。</summary>
    [JsonPropertyName("x")]
    public int X { get; set; }

    /// <summary>左上角 Y 坐标（像素）。</summary>
    [JsonPropertyName("y")]
    public int Y { get; set; }

    /// <summary>元素宽度（像素）。</summary>
    [JsonPropertyName("width")]
    public int Width { get; set; }

    /// <summary>元素高度（像素）。</summary>
    [JsonPropertyName("height")]
    public int Height { get; set; }

    /// <summary>元素状态字符串（normal/disabled/selected 等，由 ParseElementState 映射为枚举）。</summary>
    [JsonPropertyName("state")]
    public string? State { get; set; }

    /// <summary>置信度（0.0~1.0），JSON 中缺失或 null 时默认 0.5。</summary>
    [JsonPropertyName("confidence")]
    public double? Confidence { get; set; }
}

/// <summary>
/// 多模态 UI 元素检测 JSON 序列化上下文 — AOT 兼容的源码生成器
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNameCaseInsensitive = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip)]
[JsonSerializable(typeof(UiDetectionResultDto))]
[JsonSerializable(typeof(UiFindResultDto))]
internal sealed partial class UiDetectionJsonContext : JsonSerializerContext;