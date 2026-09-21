namespace JoinCode.Abstractions.Mcp.Protocol;

/// <summary>MCP 调用工具请求参数。</summary>
public sealed record CallToolRequestParams {
    /// <summary>获取或设置工具名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>获取或设置工具调用参数。</summary>
    [JsonPropertyName("arguments")]
    public JsonElement? Arguments { get; set; }
}

/// <summary>MCP 调用工具结果。</summary>
public sealed record CallToolResult {
    /// <summary>获取或设置内容列表。</summary>
    [JsonPropertyName("content")]
    public List<McpToolContent> Content { get; set; } = [];

    /// <summary>获取或设置是否为错误结果。</summary>
    [JsonPropertyName("isError")]
    public bool IsError { get; set; }
}

/// <summary>MCP 工具内容项。</summary>
public sealed record McpToolContent {
    /// <summary>获取或设置内容类型。</summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    /// <summary>获取或设置文本内容。</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; set; }

    /// <summary>获取或设置二进制数据。</summary>
    [JsonPropertyName("data")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Data { get; set; }

    /// <summary>获取或设置 MIME 类型。</summary>
    [JsonPropertyName("mimeType")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MimeType { get; set; }
}
