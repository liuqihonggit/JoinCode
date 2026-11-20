namespace JoinCode.Abstractions.LLM.Execution;

/// <summary>
/// QueryStreamChunk → ChatStreamEvent 适配器 — TASK103 循环合并的事件协议桥。
/// <para>
/// QueryEngine 产出 <see cref="QueryStreamChunk"/>，ChatService/GUI 消费 <see cref="ChatStreamEvent"/>；
/// 适配器是两条链路事件语义的唯一转换点，禁止在消费方各自手写 switch。
/// </para>
/// <para>
/// 无对应事件时返回 null（如 ThinkingEnd）：调用方跳过即可。
/// Error 映射为 <see cref="ChatStreamEvent.SystemNotice"/>，保证失败原因上屏而非静默丢弃。
/// </para>
/// </summary>
public static class QueryStreamChunkAdapter {
    /// <summary>
    /// 将查询流块转换为聊天流事件，无对应映射时返回 null。
    /// </summary>
    /// <param name="chunk">查询引擎产出的流式块。</param>
    /// <returns>对应的聊天流事件；无映射时为 null。</returns>
    public static ChatStreamEvent? ToChatStreamEvent(QueryStreamChunk chunk) {
        ArgumentNullException.ThrowIfNull(chunk);

        return chunk.Type switch {
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

            AgentStreamChunkType.Complete => ChatStreamEvent.Done(chunk.Usage, chunk.ModelId, chunk.StopReason),

            AgentStreamChunkType.Error => ChatStreamEvent.SystemNotice(chunk.Content ?? string.Empty),

            _ => null
        };
    }
}
