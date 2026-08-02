namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// Goal Graph 节点 Payload — 携带执行所需的所有信息
/// </summary>
public sealed class GoalNodePayload
{
    public required GoalNodeKind Kind { get; init; }
    public required string Name { get; init; }
    public string? SystemPrompt { get; init; }
    public string? Instruction { get; init; }
    public bool FreshContext { get; init; }
    public GoalNodeStatus Status { get; set; } = GoalNodeStatus.Pending;
    public string? Input { get; set; }
    public string? Output { get; set; }
    public string[]? Routes { get; set; }
    public RouteMatchMode RouteMatchMode { get; init; } = RouteMatchMode.ConditionalOnly;
    public string? ErrorMessage { get; set; }
    public int TimeoutSeconds { get; init; } = 300;
    public int? TokenBudget { get; init; }
    public int MinSuccessfulInputs { get; init; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int TokensUsed { get; set; }
    public int TurnsCompleted { get; set; }
}
