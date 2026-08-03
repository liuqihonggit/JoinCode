namespace JoinCode.Abstractions.Interfaces;

using JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent 角色注册表 — 管理 AgentRoleProfile 的注册和查询
/// 内置 Profile 在 DI 配置时注册，用户/项目自定义 Profile 运行时追加
/// </summary>
public interface IAgentRoleRegistry
{
    /// <summary>
    /// 注册角色档案
    /// </summary>
    void Register(AgentRoleProfile profile);

    /// <summary>
    /// 获取角色档案 — Coordinator 无 Variant，Executor 必须指定 Variant
    /// </summary>
    AgentRoleProfile? GetProfile(AgentRole role, ExecutorVariant? variant = null);

    /// <summary>
    /// 获取所有已注册的角色档案
    /// </summary>
    IReadOnlyList<AgentRoleProfile> GetAllProfiles();

    /// <summary>
    /// 获取指定角色的所有档案
    /// </summary>
    IReadOnlyList<AgentRoleProfile> GetProfilesByRole(AgentRole role);

    /// <summary>
    /// 获取所有可用的执行者变体
    /// </summary>
    IReadOnlyList<ExecutorVariant> GetAvailableVariants();

    /// <summary>
    /// 清除缓存（用户/项目自定义档案重新加载时调用）
    /// </summary>
    void ClearCache();
}
