
namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 工厂 — 对齐 TS 原版 createSubagentContext: fork AgentBase + 过滤工具
/// 所有子代理都是 AgentBase 实例，通过 SubAgentOptions.AllowedTools/DeniedTools 过滤工具集
/// variant 只影响系统提示词，不影响管道
/// </summary>
public static class AgentFactory
{
    /// <summary>
    /// 创建 AgentBase 实例 — 主代理和子代理走同一个类、同一条管道
    /// 工具集通过 SubAgentOptions.AllowedTools/DeniedTools 过滤
    /// </summary>
    public static AgentBase Create(
        string task,
        SubAgentOptions? options,
        IQueryEngine queryEngine,
        ILogger? logger,
        IClockService? clock = null,
        string? name = null,
        AgentRole role = AgentRole.Executor,
        ExecutorVariant? variant = null,
        ObjectId? parentObjectId = default,
        string? systemPrompt = null,
        string? instruction = null,
        bool freshContext = false,
        int? tokenBudget = null,
        string? goalId = null,
        string? graphNodeId = null,
        ObjectId sessionId = default,
        IChatContextManager? contextManager = null,
        string? customUniqueId = null)
    {
        return new AgentBase(
            task, options, queryEngine, logger, clock, name, role, variant,
            parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
            goalId, graphNodeId, sessionId, contextManager, customUniqueId);
    }
}
