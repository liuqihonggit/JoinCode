namespace Services.Api;

public sealed class ApiErrorResponse
{
    [JsonPropertyName("error")]
    public ApiErrorDetail? Error { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public sealed class ApiErrorDetail
{
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

public sealed class TokenUsageResponse
{
    [JsonPropertyName("usage")]
    public TokenUsageDetail? Usage { get; init; }
}

/// <summary>
/// Token 用量 API 响应详情 — 兼容 OpenAI (prompt_tokens/completion_tokens) 和 Anthropic (input_tokens/output_tokens) 命名
/// 统一映射到 <see cref="JoinCode.Abstractions.LLM.Chat.TokenUsage"/>
/// </summary>
public sealed class TokenUsageDetail
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; init; }

    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; init; }

    [JsonPropertyName("cache_creation_input_tokens")]
    public int? CacheCreationInputTokens { get; init; }

    [JsonPropertyName("cache_read_input_tokens")]
    public int? CacheReadInputTokens { get; init; }
}

[JsonSerializable(typeof(ApiErrorResponse))]
[JsonSerializable(typeof(ApiErrorDetail))]
[JsonSerializable(typeof(TokenUsageResponse))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
internal sealed partial class ApiJsonContext : JsonSerializerContext;
