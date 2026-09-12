namespace JoinCode.Abstractions.Mcp.Protocol;

public class McpResource
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }
}

public class McpResourceContent
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }

    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    [JsonPropertyName("blob")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Blob { get; init; }
}

public class McpResourcesListResponse
{
    [JsonPropertyName("resources")]
    public List<McpResource> Resources { get; init; } = new();
}

public class McpResourceReadRequestParams
{
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;
}

public class McpResourceReadResponse
{
    [JsonPropertyName("contents")]
    public List<McpResourceContent> Contents { get; init; } = new();
}
