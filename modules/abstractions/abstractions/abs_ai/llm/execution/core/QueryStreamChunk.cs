namespace JoinCode.Abstractions.LLM.Execution;

public sealed class QueryStreamChunk {
    /// <summary>获取流式块类型。</summary>
    public AgentStreamChunkType Type { get; init; }
    /// <summary>获取文本内容。</summary>
    public string? Content { get; init; }
    /// <summary>获取思考内容。</summary>
    public string? ThinkingContent { get; init; }
    /// <summary>获取工具名称。</summary>
    public string? ToolName { get; init; }
    /// <summary>获取工具调用标识。</summary>
    public string? ToolCallId { get; init; }
    /// <summary>获取工具调用参数。</summary>
    public string? ToolArguments { get; init; }
    /// <summary>获取工具调用序号。</summary>
    public int? ToolCallNumber { get; init; }
    /// <summary>获取工具调用结果。</summary>
    public ToolResult? ToolResult { get; init; }
    /// <summary>获取工具结果文本。</summary>
    public string? ToolResultText { get; init; }
    /// <summary>获取是否为工具错误。</summary>
    public bool IsToolError { get; init; }
    /// <summary>获取结构化补丁块。</summary>
    public StructuredPatchHunk[]? StructuredPatch { get; init; }
    /// <summary>获取进度消息。</summary>
    public string? ProgressMessage { get; init; }
    /// <summary>获取进度类型。</summary>
    public string? ProgressType { get; init; }
    /// <summary>获取循环触发次数。</summary>
    public int LoopTriggerCount { get; init; }
    /// <summary>获取循环起始索引。</summary>
    public int LoopStartIndex { get; init; }
    /// <summary>获取执行耗时（毫秒）。</summary>
    public long? ExecutionTimeMs { get; init; }
    /// <summary>获取令牌使用情况。</summary>
    public TokenUsage? Usage { get; init; }
    /// <summary>获取模型标识。</summary>
    public string? ModelId { get; init; }
    /// <summary>获取工具调用总数。</summary>
    public int TotalToolCalls { get; init; }
    /// <summary>获取美元成本。</summary>
    public decimal CostUsd { get; init; }
    /// <summary>获取缓存安全参数。</summary>
    public CacheSafeParams? CacheSafeParams { get; init; }
}