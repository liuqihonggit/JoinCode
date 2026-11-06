namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具参数模式
/// </summary>
public class ToolSchema {
    /// <summary>获取模式类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "object";

    /// <summary>获取属性字典。</summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, ToolSchemaProperty> Properties { get; init; } = new();

    /// <summary>获取必填属性名列表。</summary>
    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string> Required { get; init; } = [];
}

/// <summary>
/// 工具模式属性
/// </summary>
public class ToolSchemaProperty {
    /// <summary>获取属性类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "string";

    /// <summary>获取属性描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>获取枚举值列表。</summary>
    [JsonPropertyName("enum")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
#pragma warning disable JCC11002
    public List<string>? Enum { get; init; }
#pragma warning restore JCC11002

    /// <summary>获取默认值。</summary>
    [JsonPropertyName("default")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? Default { get; init; }

    /// <summary>获取数组项的模式。</summary>
    [JsonPropertyName("items")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ToolSchemaProperty? Items { get; init; }
}
