namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// Goal Graph 节点 Payload — 携带执行所需的所有信息
/// </summary>
public sealed class GoalNodePayload
{
    public required GoalNodeKind Kind { get; init; }
    public required string Name { get; init; }
    public string? AgentId { get; set; }
    public bool IsSubAgent { get; init; }

    /// <summary>
    /// Agent 类型 — 对齐 AgentDefinition.AgentType
    /// 非空时通过 IAgentService 执行（完整基础设施）
    /// 为空时回退到 SystemPrompt + Instruction 轻量模式（IChatClient 直接调用）
    /// </summary>
    public string? AgentType { get; init; }

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
