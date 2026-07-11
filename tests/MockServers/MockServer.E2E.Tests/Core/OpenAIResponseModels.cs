
namespace MockServer.E2E.Tests.Core;

/// <summary>
/// OpenAI Chat Completion 响应模型
/// </summary>
public class ChatCompletionResponse
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public List<Choice> Choices { get; set; } = new();

    [JsonPropertyName("usage")]
    public Usage? Usage { get; set; }
}

/// <summary>
/// 选择项模型
/// </summary>
public class Choice
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("message")]
    public ApiMessage? Message { get; set; }

    [JsonPropertyName("delta")]
    public ApiMessage? Delta { get; set; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; set; }
}

/// <summary>
/// Token 使用量模型
/// </summary>
public class Usage
{
    [JsonPropertyName("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonPropertyName("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonPropertyName("total_tokens")]
    public int TotalTokens { get; set; }
}

/// <summary>
/// 流式响应块模型
/// </summary>
public class ChatCompletionChunk
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion.chunk";

    [JsonPropertyName("created")]
    public long Created { get; set; }

    [JsonPropertyName("model")]
    public string Model { get; set; } = string.Empty;

    [JsonPropertyName("choices")]
    public List<Choice> Choices { get; set; } = new();
}

/// <summary>
/// 完成原因常量
/// </summary>
public static class FinishReasons
{
    public const string Stop = OpenAIFinishReasonConstants.Stop;
    public const string Length = OpenAIFinishReasonConstants.Length;
    public const string ContentFilter = OpenAIFinishReasonConstants.ContentFilter;
    public const string ToolCalls = OpenAIFinishReasonConstants.ToolCalls;
}
