namespace JoinCode.Abstractions.LLM.Chat;

public class TokenUsage {
    /// <summary>获取或设置提示令牌数。</summary>
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    /// <summary>获取或设置补全令牌数。</summary>
    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    /// <summary>获取总令牌数。</summary>
    [JsonPropertyName("total_tokens")]
    public int TotalTokens => PromptTokens + CompletionTokens;

    /// <summary>获取或设置缓存创建输入令牌数。</summary>
    [JsonPropertyName("cache_creation_input_tokens")]
    public int CacheCreationInputTokens { get; set; }

    /// <summary>获取或设置缓存读取输入令牌数。</summary>
    [JsonPropertyName("cache_read_input_tokens")]
    public int CacheReadInputTokens { get; set; }

    /// <summary>获取或设置推理令牌数。</summary>
    [JsonPropertyName("reasoning_tokens")]
    public int ReasoningTokens { get; set; }

    /// <summary>构造 TokenUsage 实例。</summary>
    public TokenUsage() { }

    /// <summary>构造 TokenUsage 实例。</summary>
    public TokenUsage(int promptTokens, int completionTokens) {
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
    }

    /// <summary>获取空实例。</summary>
    public static readonly TokenUsage Empty = new();
}