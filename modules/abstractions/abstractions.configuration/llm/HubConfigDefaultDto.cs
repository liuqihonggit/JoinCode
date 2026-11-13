namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>/config/default 端点响应 DTO — 从 Hub 获取默认供应商配置。</summary>
public sealed class HubConfigDefaultResponse {
    /// <summary>供应商配置字典。</summary>
    [JsonPropertyName("vendor")]
    public Dictionary<string, VendorConfigEntry> Vendor { get; init; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>当前默认供应商。</summary>
    [JsonPropertyName("currentProfile")]
    public string CurrentProfile { get; init; } = string.Empty;
}

/// <summary>供应商配置条目 — /config/default 端点返回的供应商信息。</summary>
public sealed class VendorConfigEntry {
    /// <summary>供应商名称。</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;

    /// <summary>协议类型。</summary>
    [JsonPropertyName("protocol")]
    public string Protocol { get; init; } = string.Empty;

    /// <summary>API 端点。</summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>API Key 环境变量名。</summary>
    [JsonPropertyName("apiKeyEnvVar")]
    public string ApiKeyEnvVar { get; init; } = string.Empty;

    /// <summary>当前模型 ID。</summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    /// <summary>模型列表。</summary>
    [JsonPropertyName("models")]
    public List<VendorModelEntry> Models { get; init; } = [];
}

/// <summary>供应商模型条目 — /config/default 端点返回的模型信息。</summary>
public sealed class VendorModelEntry {
    /// <summary>模型 ID。</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>显示名称。</summary>
    [JsonPropertyName("displayName")]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>上下文窗口大小。</summary>
    [JsonPropertyName("contextWindow")]
    public int ContextWindow { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HubConfigDefaultResponse))]
[JsonSerializable(typeof(VendorConfigEntry))]
[JsonSerializable(typeof(VendorModelEntry))]
public partial class HubConfigJsonContext : JsonSerializerContext;
