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

public class InputSchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "object";

    [JsonPropertyName("properties")]
    public Dictionary<string, PropertySchema> Properties { get; set; } = [];

    [JsonPropertyName("required")]
    public List<string> Required { get; set; } = [];
}

public class PropertySchema
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
