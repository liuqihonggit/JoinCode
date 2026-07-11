namespace JoinCode.Abstractions.Models.Agent;

public sealed class SubAgentResult
{
    public required string AgentId { get; init; }
    public bool IsSuccess { get; init; }
    public required string Output { get; init; }
    public string? Error { get; init; }
    public long? ExecutionTimeMs { get; init; }
    public CacheSafeParams? CacheSafeParams { get; init; }
}
