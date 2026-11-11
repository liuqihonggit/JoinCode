namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 子智能体流式输出块类型（已合并 QueryStreamChunkType）
/// </summary>
public enum AgentStreamChunkType {
    /// <summary>文本内容</summary>
    [EnumValue("content")] Content,
    /// <summary>思考开始</summary>
    [EnumValue("thinking_start")] ThinkingStart,
    /// <summary>思考内容</summary>
    [EnumValue("thinking")] Thinking,
    /// <summary>思考结束</summary>
    [EnumValue("thinking_end")] ThinkingEnd,
    /// <summary>工具调用开始</summary>
    [EnumValue("tool_call_start")] ToolCallStart,
    /// <summary>工具调用结束</summary>
    [EnumValue("tool_call_end")] ToolCallEnd,
    /// <summary>工具进度 — 对齐 ChatStreamEventType.ToolProgress</summary>
    [EnumValue("tool_progress")] ToolProgress,
    /// <summary>循环检测 — 对齐 ChatStreamEventType.LoopDetected</summary>
    [EnumValue("loop_detected")] LoopDetected,
    /// <summary>计时摘要 — 对齐 ChatStreamEventType.TimingSummary</summary>
    [EnumValue("timing_summary")] TimingSummary,
    /// <summary>执行完成</summary>
    [EnumValue("complete")] Complete,
    /// <summary>执行错误</summary>
    [EnumValue("error")] Error
}
