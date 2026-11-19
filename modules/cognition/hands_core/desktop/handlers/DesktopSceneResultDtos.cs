namespace Tools.Handlers;

/// <summary>
/// 桌面场景工具建议的下一步 — 引导 AI 后续调用哪个工具及原因
/// </summary>
internal sealed record DesktopSuggestedNext {
    /// <summary>推荐调用的工具名</summary>
    [JsonPropertyName("tool")] public required string Tool { get; init; }

    /// <summary>推荐原因</summary>
    [JsonPropertyName("reason")] public required string Reason { get; init; }

    /// <summary>参数提示（可选）</summary>
    [JsonPropertyName("params_hint")] public string? ParamsHint { get; init; }
}

/// <summary>desktop_click 工具结果</summary>
internal sealed record DesktopClickResult {
    /// <summary>场景 ID</summary>
    [JsonPropertyName("scene_id")] public required string SceneId { get; init; }

    /// <summary>是否点击成功</summary>
    [JsonPropertyName("succeeded")] public required bool Succeeded { get; init; }

    /// <summary>点击的 X 坐标</summary>
    [JsonPropertyName("x")] public int? X { get; init; }

    /// <summary>点击的 Y 坐标</summary>
    [JsonPropertyName("y")] public int? Y { get; init; }

    /// <summary>失败原因</summary>
    [JsonPropertyName("error")] public string? Error { get; init; }

    /// <summary>推荐的下一步</summary>
    [JsonPropertyName("suggested_next")] public required IReadOnlyList<DesktopSuggestedNext> SuggestedNext { get; init; }
}

/// <summary>desktop_detect 识别到的 UI 元素</summary>
internal sealed record DesktopDetectedElementDto {
    /// <summary>元素类型（button/input 等）</summary>
    [JsonPropertyName("type")] public required string Type { get; init; }

    /// <summary>元素标签文本</summary>
    [JsonPropertyName("label")] public required string Label { get; init; }

    /// <summary>元素所在格子编码</summary>
    [JsonPropertyName("cell")] public required string Cell { get; init; }
}

/// <summary>desktop_detect 工具结果</summary>
internal sealed record DesktopDetectResult {
    /// <summary>场景 ID</summary>
    [JsonPropertyName("scene_id")] public required string SceneId { get; init; }

    /// <summary>识别到的 UI 元素列表</summary>
    [JsonPropertyName("elements")] public required IReadOnlyList<DesktopDetectedElementDto> Elements { get; init; }

    /// <summary>推荐的下一步</summary>
    [JsonPropertyName("suggested_next")] public required IReadOnlyList<DesktopSuggestedNext> SuggestedNext { get; init; }
}

/// <summary>desktop_look 工具结果</summary>
internal sealed record DesktopLookResult {
    /// <summary>场景 ID</summary>
    [JsonPropertyName("scene_id")] public required string SceneId { get; init; }

    /// <summary>带网格标注的截图（Base64）</summary>
    [JsonPropertyName("screenshot")] public required string Screenshot { get; init; }

    /// <summary>截图宽度</summary>
    [JsonPropertyName("image_width")] public required int ImageWidth { get; init; }

    /// <summary>截图高度</summary>
    [JsonPropertyName("image_height")] public required int ImageHeight { get; init; }

    /// <summary>四叉树当前深度</summary>
    [JsonPropertyName("depth")] public required int Depth { get; init; }

    /// <summary>推荐的下一步</summary>
    [JsonPropertyName("suggested_next")] public required IReadOnlyList<DesktopSuggestedNext> SuggestedNext { get; init; }
}

/// <summary>夹逼历史条目</summary>
internal sealed record DesktopZoomHistoryDto {
    /// <summary>层数</summary>
    [JsonPropertyName("depth")] public required int Depth { get; init; }

    /// <summary>格子编码</summary>
    [JsonPropertyName("cell")] public required string Cell { get; init; }

    /// <summary>选择的象限</summary>
    [JsonPropertyName("quadrant")] public required int Quadrant { get; init; }
}

/// <summary>desktop_scene_status 工具结果</summary>
internal sealed record DesktopStatusResult {
    /// <summary>场景 ID</summary>
    [JsonPropertyName("scene_id")] public required string SceneId { get; init; }

    /// <summary>当前四叉树层数</summary>
    [JsonPropertyName("current_depth")] public required int CurrentDepth { get; init; }

    /// <summary>当前格子编码</summary>
    [JsonPropertyName("current_cell")] public required string CurrentCell { get; init; }

    /// <summary>最后执行的动作</summary>
    [JsonPropertyName("last_action")] public required string LastAction { get; init; }

    /// <summary>夹逼历史路径</summary>
    [JsonPropertyName("zoom_history")] public required IReadOnlyList<DesktopZoomHistoryDto> ZoomHistory { get; init; }

    /// <summary>推荐的下一步</summary>
    [JsonPropertyName("suggested_next")] public required IReadOnlyList<DesktopSuggestedNext> SuggestedNext { get; init; }
}

/// <summary>desktop_zoom 工具结果</summary>
internal sealed record DesktopZoomResult {
    /// <summary>场景 ID</summary>
    [JsonPropertyName("scene_id")] public required string SceneId { get; init; }

    /// <summary>缩放后的子图（Base64）</summary>
    [JsonPropertyName("sub_image")] public required string SubImage { get; init; }

    /// <summary>当前格子编码</summary>
    [JsonPropertyName("current_cell")] public required string CurrentCell { get; init; }

    /// <summary>当前四叉树深度</summary>
    [JsonPropertyName("current_depth")] public required int CurrentDepth { get; init; }

    /// <summary>区域宽度</summary>
    [JsonPropertyName("region_width")] public required int RegionWidth { get; init; }

    /// <summary>区域高度</summary>
    [JsonPropertyName("region_height")] public required int RegionHeight { get; init; }

    /// <summary>是否已清晰到可识别粒度</summary>
    [JsonPropertyName("is_clear_enough")] public required bool IsClearEnough { get; init; }

    /// <summary>推荐的下一步</summary>
    [JsonPropertyName("suggested_next")] public required IReadOnlyList<DesktopSuggestedNext> SuggestedNext { get; init; }
}

/// <summary>
/// 桌面场景工具结果 JSON 序列化上下文 — AOT 兼容
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(DesktopClickResult))]
[JsonSerializable(typeof(DesktopDetectResult))]
[JsonSerializable(typeof(DesktopLookResult))]
[JsonSerializable(typeof(DesktopStatusResult))]
[JsonSerializable(typeof(DesktopZoomResult))]
internal sealed partial class DesktopSceneToolJsonContext : JsonSerializerContext;
