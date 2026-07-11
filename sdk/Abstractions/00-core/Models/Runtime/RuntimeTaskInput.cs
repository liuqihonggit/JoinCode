namespace JoinCode.Abstractions.Models.Runtime;

public sealed record RuntimeTaskInput
{
    public required string Description { get; init; }
    public RuntimeTaskPriority Priority { get; init; } = RuntimeTaskPriority.Later;
    public string? GoalId { get; init; }
    public string? AgentId { get; init; }
    public List<string>? Dependencies { get; init; }
    public string? CronExpression { get; init; }
    public bool IsDurable { get; init; }
    public bool IsLightweight { get; init; }
    public int MaxRetries { get; init; } = 2;
}
