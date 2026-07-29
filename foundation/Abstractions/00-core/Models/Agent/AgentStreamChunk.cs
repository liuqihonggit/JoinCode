namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 子智能体流式输出块 — 对齐 TS runAgent AsyncGenerator yield Message
/// 用于实时报告子智能体的执行进度
/// </summary>
public sealed class AgentStreamChunk
{
    /// <summary>
    /// 块类型
    /// </summary>
    public required AgentStreamChunkType Type { get; init; }

    /// <summary>
    /// 文本内容（Content/Complete/Error 类型）
    /// </summary>
    public string? Content { get; init; }

    /// <summary>
    /// 工具名称（ToolCallStart/ToolCallEnd 类型）
    /// </summary>
    public string? ToolName { get; init; }

    /// <summary>
    /// 工具调用序号
    /// </summary>
    public int? ToolCallNumber { get; init; }

    /// <summary>
    /// 工具执行结果（ToolCallEnd 类型）
    /// </summary>
    public ToolResult? ToolResult { get; init; }

    /// <summary>
    /// 执行时间（毫秒）
    /// </summary>
    public long? ExecutionTimeMs { get; init; }

    /// <summary>
    /// 子智能体 ID
    /// </summary>
    public required string AgentId { get; init; }
}

/// <summary>
/// 子智能体流式输出块类型（已合并 QueryStreamChunkType）
/// </summary>
public enum AgentStreamChunkType
{
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
    /// <summary>执行完成</summary>
    [EnumValue("complete")] Complete,
    /// <summary>执行错误</summary>
    [EnumValue("error")] Error
}
