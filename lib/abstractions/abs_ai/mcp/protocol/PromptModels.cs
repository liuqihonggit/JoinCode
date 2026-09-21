namespace JoinCode.Abstractions.Mcp.Protocol;

public class McpPrompt {
    /// <summary>获取提示名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>获取提示描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>获取参数列表。</summary>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<McpPromptArgument> Arguments { get; init; } = [];
}

public class McpPromptArgument {
    /// <summary>获取参数名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>获取参数描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>获取是否必填。</summary>
    [JsonPropertyName("required")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Required { get; init; }
}

public class McpPromptMessage {
    /// <summary>获取提示消息描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>获取消息列表。</summary>
    [JsonPropertyName("messages")]
    public List<McpMessage> Messages { get; init; } = new();
}

public class McpMessage {
    /// <summary>获取角色（user/assistant）。</summary>
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    /// <summary>获取消息内容。</summary>
    [JsonPropertyName("content")]
    public McpMessageContent Content { get; init; } = new();
}

public class McpMessageContent {
    /// <summary>获取内容类型（text/image 等）。</summary>
    [JsonPropertyName("type")]
    public string Type { get; init; } = "text";

    /// <summary>获取文本内容。</summary>
    [JsonPropertyName("text")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Text { get; init; }
}

public class McpPromptGetRequestParams {
    /// <summary>获取提示名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    /// <summary>获取参数字典。</summary>
    [JsonPropertyName("arguments")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, string> Arguments { get; init; } = [];
}

public class McpPromptsListResponse {
    /// <summary>获取提示列表。</summary>
    [JsonPropertyName("prompts")]
    public List<McpPrompt> Prompts { get; init; } = new();
}

public class McpPromptGetResponse {
    /// <summary>获取响应描述。</summary>
    [JsonPropertyName("description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; init; }

    /// <summary>获取消息列表。</summary>
    [JsonPropertyName("messages")]
    public List<McpMessage> Messages { get; init; } = new();
}