namespace JoinCode.Abstractions.Schema;

/// <summary>
/// 输入 Schema 基类 — 提取 InputSchema、OpenAIFunctionParameters、AnthropicInputSchema 共同的 Type + Required 模式
/// </summary>
public abstract class InputSchemaBase
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Required { get; set; }
}
