namespace MockAiResponder;

/// <summary>
/// MockServer /__control__/requests 返回的请求 DTO（snake_case JSON）
/// </summary>
internal sealed class CapturedRequestDto {
    public int Index { get; set; }
    public string Method { get; set; } = "";
    public string Path { get; set; } = "";
    public string Body { get; set; } = "";
}

/// <summary>
/// /__control__/inject 请求体（对应 MockServer.Core.InjectedResponse，snake_case）
/// </summary>
internal sealed class InjectDto {
    public int StatusCode { get; set; } = 200;
    public string ContentType { get; set; } = "application/json";
    public string Body { get; set; } = "{}";
    public bool IsStream { get; set; }
    public List<string> StreamChunks { get; set; } = [];
}

/// <summary>OpenAI chat.completion 响应 DTO</summary>
internal sealed class OpenAiResponse {
    public string Id { get; set; } = "";
    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion";
    public string Model { get; set; } = "mock-ai";
    public List<OpenAiChoice> Choices { get; set; } = [];
    public OpenAiUsage Usage { get; set; } = new();
}

internal sealed class OpenAiChoice {
    public int Index { get; set; }
    public OpenAiMessage Message { get; set; } = new();
    public string FinishReason { get; set; } = "stop";
}

internal sealed class OpenAiMessage {
    public string Role { get; set; } = "assistant";
    public string Content { get; set; } = "";
}

internal sealed class OpenAiUsage {
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

/// <summary>OpenAI chat.completion.chunk 流式 DTO</summary>
internal sealed class StreamChunk {
    public string Id { get; set; } = "";
    [JsonPropertyName("object")]
    public string Object { get; set; } = "chat.completion.chunk";
    public List<StreamChoice> Choices { get; set; } = [];
}

internal sealed class StreamChoice {
    public int Index { get; set; }
    public StreamDelta Delta { get; set; } = new();
    public string? FinishReason { get; set; }
}

internal sealed class StreamDelta {
    public string? Role { get; set; }
    public string? Content { get; set; }
}
