namespace JoinCode.Abstractions.LLM.Chat;

public sealed class StreamEvent {
    /// <summary>获取消息角色。</summary>
    public MessageRole? Role { get; init; }
    /// <summary>获取内容。</summary>
    public string? Content { get; init; }
    /// <summary>获取模型标识。</summary>
    public string? ModelId { get; init; }
    /// <summary>获取元数据字典。</summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; init; } = new Dictionary<string, JsonElement>();

    /// <summary>构造流事件默认实例。</summary>
    public StreamEvent() { }

    /// <summary>构造流事件。</summary>
    public StreamEvent(MessageRole? role, string? content, string? modelId = null, IReadOnlyDictionary<string, JsonElement>? metadata = null) {
        Role = role;
        Content = content;
        ModelId = modelId;
        Metadata = metadata ?? new Dictionary<string, JsonElement>();
    }
}