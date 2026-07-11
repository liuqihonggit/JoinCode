namespace JoinCode.Abstractions.Models.Runtime;

public sealed record RuntimeTask
{
    public required string Id { get; init; }
    public required string Description { get; set; }
    public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.Pending;
    public RuntimeTaskPriority Priority { get; set; } = RuntimeTaskPriority.Later;
    public string? GoalId { get; init; }
    public string? AgentId { get; set; }
    public List<string> Dependencies { get; init; } = [];
    public string? CronExpression { get; init; }
    public bool IsDurable { get; init; }
    public bool IsLightweight { get; init; }
    public int RetryCount { get; set; }
    public int MaxRetries { get; init; } = 2;
    public string? Result { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    public bool CanRetry => RetryCount < MaxRetries && Status == TaskExecutionStatus.Failed;

    public TimeSpan? Duration => StartedAt.HasValue && CompletedAt.HasValue
        ? CompletedAt.Value - StartedAt.Value
        : null;
}
