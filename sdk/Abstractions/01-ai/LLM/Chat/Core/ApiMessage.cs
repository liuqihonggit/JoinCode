namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ApiMessage
{
    public MessageRole Role { get; init; }
    public string? Content { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }
    public string? ModelId { get; init; }
    public TokenUsage? TokenUsage { get; init; }

    /// <summary>
    /// 多模态内容块 — 对齐 TS Anthropic ContentBlock (image/document)
    /// 当工具结果包含图片或二进制内容时，此字段承载非文本内容块
    /// ChatService 负责将这些内容块转换为 LLM API 的多模态格式
    /// </summary>
    public IReadOnlyList<ToolContent>? ContentBlocks { get; init; }

    public ApiMessage() { }

    public ApiMessage(MessageRole role, string? content, IReadOnlyDictionary<string, JsonElement>? metadata = null, string? modelId = null, TokenUsage? tokenUsage = null)
    {
        Role = role;
        Content = content;
        Metadata = metadata;
        ModelId = modelId;
        TokenUsage = tokenUsage;
    }
}
