namespace McpClient;

/// <summary>
/// MCPB 包清单 — 描述 MCPB 插件包的元数据与服务器配置
/// </summary>
public sealed partial record McpbManifest {
    /// <summary>包名称</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>包版本</summary>
    [JsonPropertyName("version")]
    public string? Version { get; init; }

    /// <summary>包描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>作者信息</summary>
    [JsonPropertyName("author")]
    public McpbAuthor? Author { get; init; }

    /// <summary>服务器配置</summary>
    [JsonPropertyName("server")]
    public McpbServerConfig? Server { get; init; }

    /// <summary>用户自定义配置选项字典</summary>
    [JsonPropertyName("user_config")]
    public Dictionary<string, McpbUserConfigOption> UserConfig { get; init; } = [];
}

/// <summary>
/// MCPB 包作者信息
/// </summary>
public sealed partial record McpbAuthor {
    /// <summary>作者名称</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

/// <summary>
/// MCPB 服务器配置 — 描述如何启动 MCP 服务器
/// </summary>
public sealed partial record McpbServerConfig {
    /// <summary>传输类型（stdio/http/sse/websocket 等）</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>启动命令（stdio 类型使用）</summary>
    [JsonPropertyName("command")]
    public string? Command { get; init; }

    /// <summary>命令参数列表</summary>
    [JsonPropertyName("args")]
    public List<string> Args { get; init; } = [];

    /// <summary>服务器 URL（http/sse/websocket 类型使用）</summary>
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>环境变量字典（值可为任意 JSON 类型）</summary>
    [JsonPropertyName("env")]
    public Dictionary<string, JsonElement> Env { get; init; } = [];
}

/// <summary>
/// MCPB 用户配置选项 — 描述单个用户可配置参数
/// </summary>
public sealed partial record McpbUserConfigOption {
    /// <summary>选项类型（string/number/boolean 等）</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>选项标题</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    /// <summary>选项描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>是否必填</summary>
    [JsonPropertyName("required")]
    public bool Required { get; init; }

    /// <summary>默认值</summary>
    [JsonPropertyName("default")]
    public JsonElement? Default { get; init; }

    /// <summary>是否为敏感信息（如密码、令牌）</summary>
    [JsonPropertyName("sensitive")]
    public bool Sensitive { get; init; }
}

/// <summary>
/// MCPB 加载结果 — 包含解析后的清单、解压路径和内容哈希
/// </summary>
public sealed partial record McpbLoadResult {
    /// <summary>解析后的清单</summary>
    public required McpbManifest Manifest { get; init; }
    /// <summary>解压后的插件路径</summary>
    public required string ExtractedPath { get; init; }
    /// <summary>包内容哈希（用于缓存键）</summary>
    public required string ContentHash { get; init; }
}

/// <summary>
/// MCPB 缓存元数据 — 持久化到解压目录的 .mcpb-metadata.json
/// </summary>
public sealed partial record McpbCacheMetadata {
    /// <summary>源路径或 URL</summary>
    [JsonPropertyName("source")]
    public string? Source { get; init; }

    /// <summary>包内容哈希</summary>
    [JsonPropertyName("content_hash")]
    public string? ContentHash { get; init; }

    /// <summary>解压后的路径</summary>
    [JsonPropertyName("extracted_path")]
    public string? ExtractedPath { get; init; }

    /// <summary>缓存时间（UTC）</summary>
    [JsonPropertyName("cached_at")]
    public DateTime CachedAt { get; init; }
}