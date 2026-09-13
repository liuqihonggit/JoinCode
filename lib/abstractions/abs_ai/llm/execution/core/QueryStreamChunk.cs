namespace JoinCode.Abstractions.LLM.Execution;

public sealed class QueryStreamChunk
{
    public AgentStreamChunkType Type { get; init; }
    public string? Content { get; init; }
    public string? ThinkingContent { get; init; }
    public string? ToolName { get; init; }
    public string? ToolCallId { get; init; }
    public string? ToolArguments { get; init; }
    public int? ToolCallNumber { get; init; }
    public ToolResult? ToolResult { get; init; }
    public string? ToolResultText { get; init; }
    public bool IsToolError { get; init; }
    public StructuredPatchHunk[]? StructuredPatch { get; init; }
    public string? ProgressMessage { get; init; }
    public string? ProgressType { get; init; }
    public int LoopTriggerCount { get; init; }
    public int LoopStartIndex { get; init; }
    public long? ExecutionTimeMs { get; init; }
    public TokenUsage? Usage { get; init; }
    public string? ModelId { get; init; }
    public int TotalToolCalls { get; init; }
    public decimal CostUsd { get; init; }
    public CacheSafeParams? CacheSafeParams { get; init; }
}
