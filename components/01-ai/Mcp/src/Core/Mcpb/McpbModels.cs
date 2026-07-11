namespace McpClient;

public sealed partial class McpbManifest
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public McpbAuthor? Author { get; set; }

    [JsonPropertyName("server")]
    public McpbServerConfig? Server { get; set; }

    [JsonPropertyName("user_config")]
    public Dictionary<string, McpbUserConfigOption>? UserConfig { get; set; }
}

public sealed partial class McpbAuthor
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }
}

public sealed partial class McpbServerConfig
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, JsonElement>? Env { get; set; }
}

public sealed partial class McpbUserConfigOption
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("default")]
    public JsonElement? Default { get; set; }

    [JsonPropertyName("sensitive")]
    public bool Sensitive { get; set; }
}

public sealed partial class McpbLoadResult
{
    public required McpbManifest Manifest { get; init; }
    public required string ExtractedPath { get; init; }
    public required string ContentHash { get; init; }
}

public sealed partial class McpbCacheMetadata
{
    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("content_hash")]
    public string? ContentHash { get; set; }

    [JsonPropertyName("extracted_path")]
    public string? ExtractedPath { get; set; }

    [JsonPropertyName("cached_at")]
    public DateTime CachedAt { get; set; }
}
