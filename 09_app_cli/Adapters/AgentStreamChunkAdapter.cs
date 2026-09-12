namespace JoinCode.Adapters;

/// <summary>
/// AgentStreamChunk → ChatStreamEvent 适配器
/// 将 AgentBase.ExecuteStreamAsync 产出的 AgentStreamChunk 转换为 SessionController/CliEventConsumer 期望的 ChatStreamEvent
/// 返回 null 表示该 chunk 类型无对应 ChatStreamEvent（如 ThinkingEnd/Error），调用方应跳过
/// </summary>
public static class AgentStreamChunkAdapter
{
    /// <summary>
    /// 将 AgentStreamChunk 转换为 ChatStreamEvent，无对应映射时返回 null
    /// </summary>
    public static ChatStreamEvent? ToChatStreamEvent(AgentStreamChunk chunk)
    {
        return chunk.Type switch
        {
            AgentStreamChunkType.Content => ChatStreamEvent.Text(chunk.Content ?? string.Empty),

            AgentStreamChunkType.ThinkingStart => ChatStreamEvent.Thinking(chunk.ThinkingContent ?? chunk.Content ?? string.Empty),
            AgentStreamChunkType.Thinking => ChatStreamEvent.Thinking(chunk.ThinkingContent ?? chunk.Content ?? string.Empty),
            AgentStreamChunkType.ThinkingEnd => null,

            AgentStreamChunkType.ToolCallStart => ChatStreamEvent.ToolStart(
                chunk.ToolName ?? string.Empty,
                chunk.ToolCallId,
                chunk.ToolArguments),

            AgentStreamChunkType.ToolCallEnd => ChatStreamEvent.ToolEnd(
                chunk.ToolName ?? string.Empty,
                chunk.ToolResultText,
                chunk.ToolCallId,
                chunk.IsToolError,
                chunk.StructuredPatch),

            AgentStreamChunkType.ToolProgress => ChatStreamEvent.ToolProgress(
                chunk.ToolName ?? string.Empty,
                chunk.ProgressType ?? string.Empty,
                chunk.ProgressMessage ?? string.Empty,
                chunk.ToolCallId),

            AgentStreamChunkType.LoopDetected => ChatStreamEvent.LoopDetected(
                chunk.LoopTriggerCount,
                chunk.LoopStartIndex,
                chunk.Content),

            AgentStreamChunkType.TimingSummary => ChatStreamEvent.TimingSummary(chunk.Content ?? string.Empty),

            AgentStreamChunkType.Complete => ChatStreamEvent.Done(chunk.Usage, chunk.ModelId),

            AgentStreamChunkType.Error => null,

            _ => null
        };
    }
}
