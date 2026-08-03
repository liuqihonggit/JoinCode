namespace JoinCode.Abstractions.Models.Goal;

<<<<<<< HEAD
using JoinCode.Abstractions.Models.Agent;

=======
>>>>>>> c0bbb415c3daaa0e27b22a271cafbff47cad1d13
/// <summary>
/// Goal Graph 节点 Payload — 携带执行所需的所有信息
/// </summary>
public sealed class GoalNodePayload
{
    public required GoalNodeKind Kind { get; init; }
    public required string Name { get; init; }
    public string? AgentId { get; set; }
<<<<<<< HEAD
    public AgentRole Role { get; init; } = AgentRole.Executor;

    /// <summary>
    /// 执行者变体 — 仅 Executor 角色有值
    /// 非空时通过 IAgentService 执行（完整基础设施）
    /// 为空时回退到 SystemPrompt + Instruction 轻量模式（IChatClient 直接调用）
    /// </summary>
    public ExecutorVariant? Variant { get; init; }

=======
    public bool IsSubAgent { get; init; }
>>>>>>> c0bbb415c3daaa0e27b22a271cafbff47cad1d13
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
<<<<<<< HEAD

    /// <summary>
    /// 当前循环迭代次数（负向评价-修复循环）
    /// </summary>
    public int LoopIteration { get; set; }

    /// <summary>
    /// 最大循环迭代次数（纵深防御硬上限，默认16）
    /// </summary>
    public int MaxLoopIterations { get; init; } = 16;

    /// <summary>
    /// 负评条数累计
    /// </summary>
    public int NegativeReviewCount { get; set; }

    /// <summary>
    /// 原始任务ID（跨对话传递，关联 mcp_task）
    /// </summary>
    public string? OriginalTaskId { get; init; }

    /// <summary>
    /// 负向评价任务ID（关联 mcp_task）
    /// </summary>
    public string? NegativeReviewTaskId { get; set; }
=======
>>>>>>> c0bbb415c3daaa0e27b22a271cafbff47cad1d13
}
