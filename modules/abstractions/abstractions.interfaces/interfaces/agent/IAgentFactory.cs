namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Agent 工厂接口 — 创建 Agent 实例的抽象工厂
/// </summary>
public interface IAgentFactory {
    /// <summary>
    /// 创建 Agent 实例
    /// </summary>
    IAgent Create(
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
        string? customUniqueId = null);
}
