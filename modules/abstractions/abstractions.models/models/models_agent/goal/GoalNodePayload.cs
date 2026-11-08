namespace JoinCode.Abstractions.Models.Goal;

/// <summary>
/// Goal Graph 节点 Payload — 携带执行所需的所有信息
/// </summary>
public sealed class GoalNodePayload {
    /// <summary>获取节点类型。</summary>
    public required GoalNodeKind Kind { get; init; }
    /// <summary>获取节点名称。</summary>
    public required string Name { get; init; }
    /// <summary>获取或设置执行 Agent 标识。</summary>
    public string? AgentId { get; set; }
    /// <summary>获取 Agent 角色。</summary>
    public AgentRole Role { get; init; } = AgentRole.Executor;

    /// <summary>
    /// 执行者变体 — 仅 Executor 角色有值
    /// 非空时通过 IAgentService 执行（完整基础设施）
    /// 为空时回退到 SystemPrompt + Instruction 轻量模式（IChatClient 直接调用）
    /// </summary>
    public ExecutorVariant? Variant { get; init; }

    /// <summary>获取 Agent 隔离模式。</summary>
    public AgentIsolationMode IsolationMode { get; init; } = AgentIsolationMode.None;

    /// <summary>
    /// 任务声明拥有的文件（计划修改的文件）— 复用于热点识别
    /// null 或空数组表示不声明拥有文件（不触发热点检查）
    /// </summary>
    public string[]? OwnedFiles { get; init; }

    /// <summary>获取或设置系统提示词。</summary>
    public string? SystemPrompt { get; init; }
    /// <summary>获取或设置执行指令。</summary>
    public string? Instruction { get; init; }
    /// <summary>获取是否使用全新上下文。</summary>
    public bool FreshContext { get; init; }
    /// <summary>获取或设置节点执行状态。</summary>
    public GoalNodeStatus Status { get; set; } = GoalNodeStatus.Pending;
    /// <summary>获取或设置输入内容。</summary>
    public string? Input { get; set; }
    /// <summary>获取或设置输出内容。</summary>
    public string? Output { get; set; }
    /// <summary>获取或设置路由目标列表。</summary>
    public string[]? Routes { get; set; }
    /// <summary>获取路由匹配模式。</summary>
    public RouteMatchMode RouteMatchMode { get; init; } = RouteMatchMode.ConditionalOnly;
    /// <summary>获取或设置错误信息。</summary>
    public string? ErrorMessage { get; set; }
    /// <summary>获取超时秒数。</summary>
    public int TimeoutSeconds { get; init; } = 300;
    /// <summary>获取 Token 预算。</summary>
    public int? TokenBudget { get; init; }
    /// <summary>获取最少成功输入数。</summary>
    public int MinSuccessfulInputs { get; init; }
    /// <summary>获取或设置开始时间。</summary>
    public DateTime? StartedAt { get; set; }
    /// <summary>获取或设置完成时间。</summary>
    public DateTime? CompletedAt { get; set; }
    /// <summary>获取或设置已使用 Token 数。</summary>
    public int TokensUsed { get; set; }
    /// <summary>获取或设置已完成轮数。</summary>
    public int TurnsCompleted { get; set; }

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
}
