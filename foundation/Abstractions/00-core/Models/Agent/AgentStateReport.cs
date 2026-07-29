namespace JoinCode.Abstractions.Models.Agent;

public sealed class AgentStateInfo
{
    public required string AgentId { get; init; }
    public required string Task { get; init; }
    public required TaskExecutionStatus CurrentState { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? CompletedAt { get; init; }
    public long? ExecutionTimeMs { get; init; }
    public SubAgentOptions? Options { get; init; }
    public JoinCode.Abstractions.Interfaces.AgentProgress? Progress { get; init; }
}

public sealed class AgentStateReport
{
    public int TotalAgents { get; init; }
    public int PendingCount { get; init; }
    public int RunningCount { get; init; }
    public int PausedCount { get; init; }
    public int CompletedCount { get; init; }
    public int FailedCount { get; init; }
    public int CancelledCount { get; init; }
    public List<AgentStateInfo> Agents { get; init; } = new();
}
