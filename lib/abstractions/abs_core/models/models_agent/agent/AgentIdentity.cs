namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent 核心身份 — 统一 AgentId + DisplayName + Role + Variant 的组合
/// 用于在类间传递 Agent 身份信息,替代4个独立参数
/// </summary>
public readonly record struct AgentCoreIdentity(
    string AgentId,
    string? DisplayName = null,
    AgentRole? Role = null,
    ExecutorVariant? Variant = null);

/// <summary>
/// 团队身份 — 统一 TeamId + TeamName 的组合
/// </summary>
public readonly record struct TeamIdentity(
    string TeamId,
    string? TeamName = null);

/// <summary>
/// 消息端点身份 — CoordinatorMessage 的 From/To AgentId 对
/// </summary>
public readonly record struct MessageEndpoints(
    string FromAgentId,
    string ToAgentId);
