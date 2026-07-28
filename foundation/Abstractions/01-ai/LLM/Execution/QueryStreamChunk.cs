namespace JoinCode.Abstractions.LLM.Execution;

public sealed class QueryStreamChunk
{
    public AgentStreamChunkType Type { get; init; }
    public string? Content { get; init; }
    public string? ToolName { get; init; }
    public int? ToolCallNumber { get; init; }
    public ToolResult? ToolResult { get; init; }
    public long? ExecutionTimeMs { get; init; }
    public int TotalToolCalls { get; init; }
    public decimal CostUsd { get; init; }
    public CacheSafeParams? CacheSafeParams { get; init; }
}
