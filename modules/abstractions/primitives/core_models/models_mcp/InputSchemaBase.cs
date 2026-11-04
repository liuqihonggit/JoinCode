namespace JoinCode.Abstractions.Schema;

/// <summary>
/// 输入 Schema 基类 — 提取 InputSchema、OpenAIFunctionParameters、AnthropicInputSchema 共同的 Type + Required 模式
/// </summary>
public abstract class InputSchemaBase {
    /// <summary>获取或设置 Schema 类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    /// <summary>获取或设置必需字段名列表。</summary>
    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string> Required { get; set; } = [];
}