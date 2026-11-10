namespace JoinCode.App.Builder;

/// <summary>Hub 服务端模型列表响应 DTO（用于从 jcchub.exe /v1/models 解析）。</summary>
public sealed class HubModelsResponse {
    /// <summary>模型列表。</summary>
    [JsonPropertyName("data")]
    public List<HubModelInfo> Data { get; init; } = [];
}

/// <summary>Hub 服务端模型信息 DTO。</summary>
public sealed class HubModelInfo {
    /// <summary>模型 ID。</summary>
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>所属 Provider。</summary>
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = string.Empty;
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(HubModelsResponse))]
public partial class HubModelsJsonContext : JsonSerializerContext;
