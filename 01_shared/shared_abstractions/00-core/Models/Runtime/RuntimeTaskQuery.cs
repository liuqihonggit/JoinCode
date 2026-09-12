namespace JoinCode.Abstractions.Models.Runtime;

public sealed record RuntimeTaskQuery
{
    public TaskExecutionStatus? Status { get; init; }
    public string? GoalId { get; init; }
    public string? AgentId { get; init; }
    public RuntimeTaskPriority? Priority { get; init; }
    public bool IncludeCompleted { get; init; }
    public int Limit { get; init; } = 50;
    public int Offset { get; init; }
}
