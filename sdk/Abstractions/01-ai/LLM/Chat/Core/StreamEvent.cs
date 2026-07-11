namespace JoinCode.Abstractions.LLM.Chat;

public sealed class StreamEvent
{
    public MessageRole? Role { get; init; }
    public string? Content { get; init; }
    public string? ModelId { get; init; }
    public IReadOnlyDictionary<string, JsonElement>? Metadata { get; init; }

    public StreamEvent() { }

    public StreamEvent(MessageRole? role, string? content, string? modelId = null, IReadOnlyDictionary<string, JsonElement>? metadata = null)
    {
        Role = role;
        Content = content;
        ModelId = modelId;
        Metadata = metadata;
    }
}
