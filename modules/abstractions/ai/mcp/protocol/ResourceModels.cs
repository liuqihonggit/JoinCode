namespace JoinCode.Abstractions.Mcp.Protocol;

public class McpResource {
    /// <summary>获取资源 URI。</summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>获取资源名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>获取资源描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>获取 MIME 类型。</summary>
    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }
}

public class McpResourceContent {
    /// <summary>获取资源 URI。</summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;

    /// <summary>获取 MIME 类型。</summary>
    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; init; }

    /// <summary>获取文本内容。</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }

    /// <summary>获取 Base64 编码的二进制内容。</summary>
    [JsonPropertyName("blob")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Blob { get; init; }
}

public class McpResourcesListResponse {
    /// <summary>获取资源列表。</summary>
    [JsonPropertyName("resources")]
    public List<McpResource> Resources { get; init; } = new();
}

public class McpResourceReadRequestParams {
    /// <summary>获取要读取的资源 URI。</summary>
    [JsonPropertyName("uri")]
    public string Uri { get; init; } = string.Empty;
}

public class McpResourceReadResponse {
    /// <summary>获取资源内容列表。</summary>
    [JsonPropertyName("contents")]
    public List<McpResourceContent> Contents { get; init; } = new();
}