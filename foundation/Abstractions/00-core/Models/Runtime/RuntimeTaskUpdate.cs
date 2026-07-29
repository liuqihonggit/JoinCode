namespace JoinCode.Abstractions.Models.Runtime;

public sealed record RuntimeTaskUpdate
{
    public string? Description { get; init; }
    public TaskExecutionStatus? Status { get; init; }
    public RuntimeTaskPriority? Priority { get; init; }
    public string? AgentId { get; init; }
    public string? Result { get; init; }
    public string? ErrorMessage { get; init; }
}
