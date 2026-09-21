namespace JoinCode.Abstractions.Mcp.Protocol;

public class ListToolsResult {
    /// <summary>获取或设置工具定义列表。</summary>
    [JsonPropertyName("tools")]
    public List<ToolDefinition> Tools { get; set; } = [];
}

public class ToolDefinition {
    /// <summary>获取或设置工具名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>获取或设置工具描述。</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>获取或设置输入参数模式。</summary>
    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; set; }

    /// <summary>获取或设置工具分类。</summary>
    [JsonPropertyName("category")]
    public string Category { get; set; } = "general";
}

public class InputSchema : InputSchemaBase {
    /// <summary>获取或设置属性字典。</summary>
    [JsonPropertyName("properties")]
    public Dictionary<string, PropertySchema> Properties { get; set; } = [];
}

public class PropertySchema : SchemaProperty {
}
