namespace JoinCode.Hands.Scenarios;

/// <summary>
/// 情景模式菜单 JSON DTO — 供 ToolMenuRenderer.ToJson 序列化，对齐 AI 消费的菜单 JSON 格式。
/// </summary>
/// <param name="Scenes">场景列表。</param>
public sealed record ScenarioMenuDto(
    [property: JsonPropertyName("scenes")] ScenarioMenuSceneDto[] Scenes);

/// <summary>
/// 单个场景菜单项 DTO。
/// </summary>
public sealed record ScenarioMenuSceneDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("description")] string Description,
    [property: JsonPropertyName("tools")] string[] Tools,
    [property: JsonPropertyName("suggested_flow")] string SuggestedFlow,
    [property: JsonPropertyName("tips")] string Tips);

/// <summary>
/// 情景模式菜单 JSON 序列化上下文 — AOT 源码生成，Relaxed 实例配置 UnsafeRelaxedJsonEncoder 不转义中文。
/// </summary>
[JsonSerializable(typeof(ScenarioMenuDto))]
internal sealed partial class ScenarioMenuJsonContext : JsonSerializerContext {
    private static readonly JsonSerializerOptions s_opts = new() {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        TypeInfoResolver = ScenarioMenuJsonContext.Default,
    };

    /// <summary>带不转义中文 encoder 的实例 — 供 ToJson 使用（源码生成器已生成带 options 构造器）。</summary>
    public static readonly ScenarioMenuJsonContext Relaxed = new(s_opts);
}
