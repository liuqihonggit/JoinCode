namespace JoinCode.Abstractions.Schema;

/// <summary>
/// Schema 属性基类 — 提取 PropertySchema、ToolParameter、OpenAIParameterProperty、AnthropicSchemaProperty、ElicitSchemaProperty 共同的 Type + Description 模式
/// </summary>
public abstract class SchemaProperty
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}
