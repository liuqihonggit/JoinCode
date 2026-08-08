namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent 角色 — 协调者、执行者、推理三权分立角色
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
    [EnumValue("executor")] Executor,

    /// <summary>
    /// 控方 — 主动寻找证据，提出指控
    /// </summary>
    [EnumValue("prosecutor")] Prosecutor,

    /// <summary>
    /// 辩方 — 质疑证据，寻找反驳
    /// </summary>
    [EnumValue("defender")] Defender,

    /// <summary>
    /// 法官 — 最终裁决，基于证据链和权重
    /// </summary>
    [EnumValue("judge")] Judge
}
