namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 内置代理类型定义 — 对齐 TS builtInAgents.ts
/// 用于 AgentDefinition.AgentType 字段的字符串映射
/// </summary>
public enum AgentTypeDefinition
{
    /// <summary>
    /// 默认代理 — 全量工具集
    /// </summary>
    [EnumValue("default")] Default,

    /// <summary>
    /// 代码代理 — 代码读写编辑
    /// </summary>
    [EnumValue("code")] Code,

    /// <summary>
    /// 搜索代理 — 代码搜索导航（只读）
    /// </summary>
    [EnumValue("search")] Search,

    /// <summary>
    /// 探索代理 — 快速代码库探索（只读，一次性）
    /// </summary>
    [EnumValue("Explore")] Explore,

    /// <summary>
    /// 计划代理 — 架构设计与实施计划（只读，一次性）
    /// </summary>
    [EnumValue("Plan")] Plan,
}

/// <summary>
/// 一次性内置代理类型 — 对齐 TS ONE_SHOT_BUILTIN_AGENT_TYPES
/// Explore/Plan 运行一次即返回报告，不会通过 SendMessage 继续
/// 结果中省略 agentId/SendMessage 提示，节省 token
/// </summary>
public static class OneShotBuiltinAgentTypes
{
    private static readonly FrozenSet<string> Types = FrozenSet.Create(
        StringComparer.Ordinal,
        AgentTypeDefinition.Explore.ToValue(),
        AgentTypeDefinition.Plan.ToValue());

    /// <summary>
    /// 判断指定代理类型是否为一次性内置代理
    /// </summary>
    public static bool IsOneShot(string agentType) => Types.Contains(agentType);
}
