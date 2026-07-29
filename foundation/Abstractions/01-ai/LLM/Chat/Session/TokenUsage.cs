namespace JoinCode.Abstractions.LLM.Chat;

public class TokenUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens => PromptTokens + CompletionTokens;

    [JsonPropertyName("cache_creation_input_tokens")]
    public int CacheCreationInputTokens { get; set; }

    [JsonPropertyName("cache_read_input_tokens")]
    public int CacheReadInputTokens { get; set; }

    [JsonPropertyName("reasoning_tokens")]
    public int ReasoningTokens { get; set; }

    public TokenUsage() { }

    public TokenUsage(int promptTokens, int completionTokens)
    {
        PromptTokens = promptTokens;
        CompletionTokens = completionTokens;
    }

    public static readonly TokenUsage Empty = new();
}
