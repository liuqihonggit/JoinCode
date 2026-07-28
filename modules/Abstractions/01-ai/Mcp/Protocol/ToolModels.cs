namespace JoinCode.Abstractions.Mcp.Protocol;

public class ListToolsResult
{
    [JsonPropertyName("tools")]
    public List<ToolDefinition> Tools { get; set; } = [];
}

public class ToolDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("inputSchema")]
    public JsonElement InputSchema { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = "general";
}

public class InputSchema : InputSchemaBase
{
    [JsonPropertyName("properties")]
    public Dictionary<string, PropertySchema> Properties { get; set; } = [];
}

public class PropertySchema : SchemaProperty
{
}
