namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent 角色 — 协调者（管理 Goal 生命周期）或执行者（执行具体任务）
/// [EnumValue] 由 EnumMetadataGenerator 自动生成 AgentRoleConstants + AgentRoleExtensions
/// </summary>
public enum AgentRole
{
    /// <summary>
    /// 协调者 — 管理 Goal 生命周期，合并 mainAgent + default 默认助手
    /// </summary>
    [EnumValue("coordinator")] Coordinator,

    /// <summary>
    /// 执行者 — 执行具体任务，通过 ExecutorVariant 区分变体
    /// </summary>
    [EnumValue("executor")] Executor
}
