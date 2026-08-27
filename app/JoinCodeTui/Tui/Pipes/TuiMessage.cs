namespace JoinCode.Tui.Pipes;

/// <summary>
/// TUI 消息 — 多 Agent 管道中的最小渲染单元。
/// 每条消息属于一个 Agent 管道（由 AgentId 标识），包含类型、内容、时间戳和样式。
/// </summary>
public sealed class TuiMessage
{
    /// <summary>消息唯一标识。</summary>
    public required string Id { get; init; }

    /// <summary>所属 Agent 管道 ID。</summary>
    public required string AgentId { get; init; }

    /// <summary>消息类型。</summary>
    public required TuiMessageType Type { get; init; }

    /// <summary>消息文本内容。</summary>
    public required string Content { get; init; }

    /// <summary>时间戳（UTC）。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>渲染样式。null 时由渲染器根据 Type 自动选择。</summary>
    public MessageStyle? Style { get; init; }

    /// <summary>是否已渲染（用于增量渲染）。</summary>
    public bool Rendered { get; set; }

    /// <summary>元数据（工具名称、子代理名称等扩展信息）。</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();

    /// <summary>从 AgentStreamChunk 创建 TuiMessage。</summary>
    public static TuiMessage FromChunk(string agentId, AgentStreamChunk chunk)
    {
        var (type, content) = chunk.Type switch
        {
            AgentStreamChunkType.Thinking or AgentStreamChunkType.ThinkingStart or AgentStreamChunkType.ThinkingEnd
                => (TuiMessageType.AgentThinking, chunk.ThinkingContent ?? chunk.Content ?? string.Empty),
            AgentStreamChunkType.Content
                => (TuiMessageType.AgentContent, chunk.Content ?? string.Empty),
            AgentStreamChunkType.ToolCallStart or AgentStreamChunkType.ToolCallEnd or AgentStreamChunkType.ToolProgress
                => (TuiMessageType.ToolCall, chunk.ToolName ?? chunk.Content ?? string.Empty),
            AgentStreamChunkType.Error
                => (TuiMessageType.Error, chunk.Content ?? string.Empty),
            AgentStreamChunkType.Complete
                => (TuiMessageType.AgentContent, chunk.Content ?? string.Empty),
            _
                => (TuiMessageType.AgentContent, chunk.Content ?? string.Empty),
        };

        return new TuiMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            AgentId = agentId,
            Type = type,
            Content = content,
            Timestamp = DateTime.UtcNow,
        };
    }
}
