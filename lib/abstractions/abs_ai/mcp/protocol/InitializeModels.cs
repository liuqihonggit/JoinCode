namespace JoinCode.Abstractions.Mcp.Protocol;

/// <summary>MCP 初始化请求参数。</summary>
public sealed record InitializeRequestParams {
    /// <summary>获取或设置协议版本。</summary>
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";

    /// <summary>获取或设置客户端能力声明。</summary>
    [JsonPropertyName("capabilities")]
    public ClientCapabilities Capabilities { get; set; } = new();

    /// <summary>获取或设置客户端实现信息。</summary>
    [JsonPropertyName("clientInfo")]
    public Implementation ClientInfo { get; set; } = new();
}

/// <summary>MCP 初始化结果。</summary>
public sealed record InitializeResult {
    /// <summary>获取或设置协议版本。</summary>
    [JsonPropertyName("protocolVersion")]
    public string ProtocolVersion { get; set; } = "2024-11-05";

    /// <summary>获取或设置服务端能力声明。</summary>
    [JsonPropertyName("capabilities")]
    public ServerCapabilities Capabilities { get; set; } = new();

    /// <summary>获取或设置服务端实现信息。</summary>
    [JsonPropertyName("serverInfo")]
    public Implementation ServerInfo { get; set; } = new();

    /// <summary>获取或设置服务端使用说明。</summary>
    [JsonPropertyName("instructions")]
    public string? Instructions { get; set; }
}

/// <summary>MCP 客户端能力声明。</summary>
public sealed record ClientCapabilities {
    /// <summary>获取或设置采样能力。</summary>
    [JsonPropertyName("sampling")]
    public JsonElement? Sampling { get; set; }

    /// <summary>获取或设置根目录能力。</summary>
    [JsonPropertyName("roots")]
    public JsonElement? Roots { get; set; }

    /// <summary>获取或设置引出能力。</summary>
    [JsonPropertyName("elicitation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Elicitation { get; set; }
}

/// <summary>MCP 服务端能力声明。</summary>
public sealed record ServerCapabilities {
    /// <summary>获取或设置工具能力。</summary>
    [JsonPropertyName("tools")]
    public ToolsCapability? Tools { get; set; }

    /// <summary>获取或设置资源能力。</summary>
    [JsonPropertyName("resources")]
    public ResourcesCapability? Resources { get; set; }

    /// <summary>获取或设置提示能力。</summary>
    [JsonPropertyName("prompts")]
    public PromptsCapability? Prompts { get; set; }

    /// <summary>获取或设置日志能力。</summary>
    [JsonPropertyName("logging")]
    public LoggingCapability? Logging { get; set; }
}

/// <summary>MCP 工具能力声明。</summary>
public sealed record ToolsCapability {
    /// <summary>获取或设置工具列表是否可变更。</summary>
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

/// <summary>MCP 资源能力声明。</summary>
public sealed record ResourcesCapability {
    /// <summary>获取或设置是否支持订阅。</summary>
    [JsonPropertyName("subscribe")]
    public bool Subscribe { get; set; }

    /// <summary>获取或设置资源列表是否可变更。</summary>
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

/// <summary>MCP 提示能力声明。</summary>
public sealed record PromptsCapability {
    /// <summary>获取或设置提示列表是否可变更。</summary>
    [JsonPropertyName("listChanged")]
    public bool ListChanged { get; set; }
}

/// <summary>MCP 日志能力声明。</summary>
public sealed record LoggingCapability {
    /// <summary>获取或设置日志级别。</summary>
    [JsonPropertyName("level")]
    public string? Level { get; set; }
}

/// <summary>MCP 实现信息。</summary>
public sealed record Implementation {
    /// <summary>获取或设置实现名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>获取或设置版本号。</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = string.Empty;
}
