namespace Services.Api;

/// <summary>
/// API 错误响应包装模型
/// </summary>
public sealed class ApiErrorResponse {
    /// <summary>
    /// 错误详情
    /// </summary>
    [JsonPropertyName("error")]
    public ApiErrorDetail? Error { get; init; }

    /// <summary>
    /// 顶层错误消息
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// API 错误详情模型
/// </summary>
public sealed class ApiErrorDetail {
    /// <summary>
    /// 错误消息
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }
}

/// <summary>
/// Token 用量 API 响应模型
/// </summary>
public sealed class TokenUsageResponse {
    /// <summary>
    /// Token 用量详情
    /// </summary>
    [JsonPropertyName("usage")]
    public TokenUsageDetail? Usage { get; init; }
}

/// <summary>
/// Token 用量 API 响应详情 — 兼容 OpenAI (prompt_tokens/completion_tokens) 和 Anthropic (input_tokens/output_tokens) 命名
/// 统一映射到 <see cref="JoinCode.Abstractions.LLM.Chat.TokenUsage"/>
/// </summary>
public sealed class TokenUsageDetail {
    /// <summary>
    /// 输入 Token 数（OpenAI 命名）
    /// </summary>
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; init; }

    /// <summary>
    /// 输入 Token 数（Anthropic 命名）
    /// </summary>
    [JsonPropertyName("input_tokens")]
    public int? InputTokens { get; init; }

    /// <summary>
    /// 输出 Token 数（OpenAI 命名）
    /// </summary>
    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; init; }

    /// <summary>
    /// 输出 Token 数（Anthropic 命名）
    /// </summary>
    [JsonPropertyName("output_tokens")]
    public int? OutputTokens { get; init; }

    /// <summary>
    /// 缓存创建输入 Token 数（Anthropic Prompt Caching）
    /// </summary>
    [JsonPropertyName("cache_creation_input_tokens")]
    public int? CacheCreationInputTokens { get; init; }

    /// <summary>
    /// 缓存读取输入 Token 数（Anthropic Prompt Caching）
    /// </summary>
    [JsonPropertyName("cache_read_input_tokens")]
    public int? CacheReadInputTokens { get; init; }
}

[JsonSerializable(typeof(ApiErrorResponse))]
[JsonSerializable(typeof(ApiErrorDetail))]
[JsonSerializable(typeof(TokenUsageResponse))]
[JsonSourceGenerationOptions(AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
internal sealed partial class ApiJsonContext : JsonSerializerContext;