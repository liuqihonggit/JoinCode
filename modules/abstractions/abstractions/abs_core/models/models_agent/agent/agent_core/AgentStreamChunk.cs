namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 智能体流式输出块 — 对齐 TS runAgent AsyncGenerator yield Message
/// 用于实时报告智能体的执行进度，主代理和子代理统一使用此类型
/// </summary>
public sealed class AgentStreamChunk {
    /// <summary>
    /// 块类型
    /// </summary>
    public required AgentStreamChunkType Type { get; init; }

    /// <summary>
    /// 文本内容（Content/Thinking/Complete/Error/TimingSummary/LoopDetected 类型）
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// 思考内容（Thinking/ThinkingStart/ThinkingEnd 类型）— 对齐 ChatStreamEvent.ThinkingContent
    /// </summary>
    public string? ThinkingContent { get; init; }

    /// <summary>
    /// 工具名称（ToolCallStart/ToolCallEnd/ToolProgress 类型）
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// 工具调用 ID（ToolCallStart/ToolCallEnd/ToolProgress 类型）— 对齐 ChatStreamEvent.ToolCallId
    /// </summary>
    public string? ToolCallId { get; init; }

    /// <summary>
    /// 工具调用参数（ToolCallStart 类型）— 对齐 ChatStreamEvent.ToolArguments
    /// </summary>
    public string? ToolArguments { get; init; }

    /// <summary>
    /// 工具调用序号
    /// </summary>
    public int? ToolCallNumber { get; init; }

    /// <summary>
    /// 工具执行结果（ToolCallEnd 类型）
    /// </summary>
    public ToolResult? ToolResult { get; init; }

    /// <summary>
    /// 工具结果文本（ToolCallEnd 类型）— 对齐 ChatStreamEvent.ToolResultText
    /// </summary>
    public string? ToolResultText { get; init; }

    /// <summary>
    /// 工具是否出错（ToolCallEnd 类型）— 对齐 ChatStreamEvent.IsToolError
    /// </summary>
    public bool IsToolError { get; init; }

    /// <summary>
    /// 结构化 Patch（ToolCallEnd 类型）— 对齐 ChatStreamEvent.StructuredPatch
    /// </summary>
    public StructuredPatchHunk[]? StructuredPatch { get; init; }

    /// <summary>
    /// 工具进度消息（ToolProgress 类型）— 对齐 ChatStreamEvent.ProgressMessage
    /// </summary>
    public string? ProgressMessage { get; init; }

    /// <summary>
    /// 进度类型标识（ToolProgress 类型）— 对齐 ChatStreamEvent.ProgressType
    /// </summary>
    public string? ProgressType { get; init; }

    /// <summary>
    /// 循环检测触发次数（LoopDetected 类型）— 对齐 ChatStreamEvent.LoopTriggerCount
    /// </summary>
    public int LoopTriggerCount { get; init; }

    /// <summary>
    /// 循环起点索引（LoopDetected 类型）— 对齐 ChatStreamEvent.LoopStartIndex
    /// </summary>
    public int LoopStartIndex { get; init; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long? ExecutionTimeMs { get; init; }

    /// <summary>
    /// Token 用量（Complete 类型）— 对齐 ChatStreamEvent.Usage
    /// </summary>
    public TokenUsage? Usage { get; init; }

    /// <summary>
    /// 模型 ID（Complete 类型）— 对齐 ChatStreamEvent.ModelId
    /// </summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// 查询终止原因（Complete 类型）— 对齐 ChatStreamEvent.StopReason
    /// </summary>
    public string? StopReason { get; init; }

    /// <summary>
    /// LLM 调用轮次数（Complete 类型）— 对齐 ClaudeCode SDK num_turns 语义
    /// </summary>
    public int TotalTurns { get; init; }

    /// <summary>
    /// 美元成本（Complete 类型）— 对齐 ClaudeCode SDK total_cost_usd 语义
    /// </summary>
    public decimal CostUsd { get; init; }

    /// <summary>
    /// 智能体 ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// 从 ChatStreamEvent 创建 AgentStreamChunk — 主代理走 ChatService 管道时使用此转换
    /// 返回 null 表示该事件类型无对应 AgentStreamChunk（如 Tombstone），调用方应跳过
    /// </summary>
    public static AgentStreamChunk? FromChatStreamEvent(ChatStreamEvent evt, string agentId) {
        return evt.Type switch {
            ChatStreamEventType.Content => new AgentStreamChunk {
                Type = AgentStreamChunkType.Content,
                Content = evt.Content,
                AgentId = agentId
            },
            ChatStreamEventType.Thinking => new AgentStreamChunk {
                Type = AgentStreamChunkType.Thinking,
                ThinkingContent = evt.ThinkingContent,
                AgentId = agentId
            },
            ChatStreamEventType.ToolCallStart => new AgentStreamChunk {
                Type = AgentStreamChunkType.ToolCallStart,
                ToolName = evt.ToolName,
                ToolCallId = evt.ToolCallId,
                ToolArguments = evt.ToolArguments,
                AgentId = agentId
            },
            ChatStreamEventType.ToolCallEnd => new AgentStreamChunk {
                Type = AgentStreamChunkType.ToolCallEnd,
                ToolName = evt.ToolName,
                ToolCallId = evt.ToolCallId,
                ToolResultText = evt.ToolResultText,
                IsToolError = evt.IsToolError,
                StructuredPatch = evt.StructuredPatch,
                AgentId = agentId
            },
            ChatStreamEventType.ToolProgress => new AgentStreamChunk {
                Type = AgentStreamChunkType.ToolProgress,
                ToolName = evt.ToolName,
                ToolCallId = evt.ToolCallId,
                ProgressMessage = evt.ProgressMessage,
                ProgressType = evt.ProgressType,
                AgentId = agentId
            },
            ChatStreamEventType.LoopDetected => new AgentStreamChunk {
                Type = AgentStreamChunkType.LoopDetected,
                LoopTriggerCount = evt.LoopTriggerCount,
                LoopStartIndex = evt.LoopStartIndex,
                Content = evt.Content,
                AgentId = agentId
            },
            ChatStreamEventType.TimingSummary => new AgentStreamChunk {
                Type = AgentStreamChunkType.TimingSummary,
                Content = evt.Content,
                AgentId = agentId
            },
            ChatStreamEventType.Complete => new AgentStreamChunk {
                Type = AgentStreamChunkType.Complete,
                Usage = evt.Usage,
                ModelId = evt.ModelId,
                StopReason = evt.StopReason,
                AgentId = agentId
            },
            _ => null
        };
    }
}

