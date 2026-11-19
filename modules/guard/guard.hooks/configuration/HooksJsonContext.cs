namespace Core.Hooks.Configuration;

/// <summary>
/// 钩子 JSON 序列化上下文 — 为钩子配置、输入、决策等类型提供 AOT 友好的源码生成序列化器
/// </summary>
[JsonSourceGenerationOptions(DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HookSettingsFile))]
[JsonSerializable(typeof(Dictionary<string, List<HookMatcher>>))]
[JsonSerializable(typeof(HookInput))]
[JsonSerializable(typeof(PermissionUpdate))]
[JsonSerializable(typeof(List<PermissionUpdate>))]
[JsonSerializable(typeof(Dictionary<string, JsonElement>))]
[JsonSerializable(typeof(HookHttpPayload))]
[JsonSerializable(typeof(HookDecision))]
[JsonSerializable(typeof(AgentHookResponseExample))]
[JsonSerializable(typeof(string))]
public partial class HooksJsonContext : JsonSerializerContext;

/// <summary>代理验证钩子期望响应示例 — 提示词内嵌 JSON 示例的唯一数据源</summary>
public sealed record AgentHookResponseExample {
    /// <summary>决策(allow/block/ask)</summary>
    [JsonPropertyName("decision")]
    public string Decision { get; init; } = "allow";
    /// <summary>详细理由</summary>
    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "detailed explanation";
    /// <summary>是否继续</summary>
    [JsonPropertyName("continue")]
    public bool Continue { get; init; } = true;
    /// <summary>置信度 0.0-1.0</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 0.9;
}