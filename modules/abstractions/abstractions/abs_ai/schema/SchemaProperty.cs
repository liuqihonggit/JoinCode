namespace JoinCode.Abstractions.Schema;

/// <summary>
/// Schema 属性基类 — 提取 PropertySchema、ToolParameter、OpenAIParameterProperty、AnthropicSchemaProperty、ElicitSchemaProperty 共同的 Type + Description 模式
/// </summary>
public abstract class SchemaProperty {
    /// <summary>获取或设置属性类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    /// <summary>获取或设置属性描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
}