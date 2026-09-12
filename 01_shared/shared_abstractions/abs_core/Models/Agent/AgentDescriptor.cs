namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 全局 Agent 描述符 — 统一描述 mainAgent 和 subAgent
/// 每个实例拥有唯一 Id、独立上下文、沟通管道
/// </summary>
public sealed class AgentDescriptor
{
    /// <summary>
    /// 唯一标识 — 格式: "agent-{Guid:N}" 前8位
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Agent 名称（如 "executor", "reviewer", "explorer"）
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// 是否为子 Agent — mainAgent=false, subAgent=true
    /// </summary>
    public bool IsSubAgent { get; init; }

    /// <summary>
    /// Agent 类型（如 "default", "code", "search"）— 对齐 AgentTypeDefinition
    /// </summary>
    public string? AgentType { get; init; }

    /// <summary>
    /// 父 Agent Id — subAgent 指向创建它的 mainAgent，mainAgent 为 null
    /// </summary>
    public string? ParentAgentId { get; init; }

    /// <summary>
    /// 当前状态
    /// </summary>
    public AgentStatus Status { get; set; } = AgentStatus.Pending;

    /// <summary>
    /// 独立对话上下文 — 每个 Agent 拥有自己的 ChatHistory
    /// </summary>
    public MessageList ChatHistory { get; } = new();

    /// <summary>
    /// 系统提示词
    /// </summary>
    public string? SystemPrompt { get; init; }

    /// <summary>
    /// 任务指令
    /// </summary>
    public string? Instruction { get; set; }

    /// <summary>
    /// 所属 GoalId — 该 Agent 服务于哪个 Goal
    /// </summary>
    public string? GoalId { get; init; }

    /// <summary>
    /// 所属 Graph 节点 Id — 该 Agent 绑定到 Graph 的哪个节点
    /// </summary>
    public string? GraphNodeId { get; init; }

    /// <summary>
    /// 是否使用全新上下文（FreshContext）— 不继承父 Agent 的 ChatHistory
    /// </summary>
    public bool FreshContext { get; init; }

    /// <summary>
    /// Token 预算
    /// </summary>
    public int? TokenBudget { get; init; }

    /// <summary>
    /// 已消耗 Token 数
    /// </summary>
    public int TokensUsed { get; set; }

    /// <summary>
    /// 已完成轮次
    /// </summary>
    public int TurnsCompleted { get; set; }

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 开始执行时间
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// 完成时间
    /// </summary>
    public DateTime? CompletedAt { get; set; }

    /// <summary>
    /// 执行输出
    /// </summary>
    public string? Output { get; set; }

    /// <summary>
    /// 错误信息
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// 条件路由结果
    /// </summary>
    public string[]? Routes { get; set; }

    /// <summary>
    /// 生成唯一 Agent Id
    /// </summary>
    public static string GenerateId() => $"agent-{Guid.NewGuid():N}"[..20];
}
