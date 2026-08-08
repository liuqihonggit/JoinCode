
namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 工厂 — 根据 ExecutorVariant 创建合适的子类实例
/// variant 为 null 时返回通用 Agent（兼容旧代码）
/// </summary>
public static class AgentFactory
{
    /// <summary>
    /// 根据 variant 创建 ExecutorAgent 子类实例
    /// variant 为 null 时返回通用 Agent
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
        IChatContextManager? contextManager = null)
    {
        if (role == AgentRole.Coordinator)
        {
            return new CoordinatorAgent(task, options, queryEngine, logger, clock, name,
                parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
                goalId, graphNodeId, sessionId, contextManager);
        }

        return variant switch
        {
            ExecutorVariant.Code => new CodeAgent(task, options, queryEngine, logger, clock, name,
                parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
                goalId, graphNodeId, sessionId, contextManager),
            ExecutorVariant.Search => new SearchAgent(task, options, queryEngine, logger, clock, name,
                parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
                goalId, graphNodeId, sessionId, contextManager),
            ExecutorVariant.Explore => new ExploreAgent(task, options, queryEngine, logger, clock, name,
                parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
                goalId, graphNodeId, sessionId, contextManager),
            ExecutorVariant.Plan => new PlanAgent(task, options, queryEngine, logger, clock, name,
                parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
                goalId, graphNodeId, sessionId, contextManager),
            ExecutorVariant.Doctor => new DoctorAgent(task, options, queryEngine, logger, clock, name,
                parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
                goalId, graphNodeId, sessionId, contextManager),
            ExecutorVariant.Verification => new VerificationAgent(task, options, queryEngine, logger, clock, name,
                parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
                goalId, graphNodeId, sessionId, contextManager),
            ExecutorVariant.ClaudeCodeGuide => new GuideAgent(task, options, queryEngine, logger, clock, name,
                parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
                goalId, graphNodeId, sessionId, contextManager),
            ExecutorVariant.ContextCompression => new ContextCompressionAgent(task, options, queryEngine, logger, clock, name,
                parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
                goalId, graphNodeId, sessionId, contextManager),
            ExecutorVariant.Teammate => new TeammateAgent(task, options, queryEngine, logger, clock, name,
                parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
                goalId, graphNodeId, sessionId, contextManager),
            _ => new CodeAgent(task, options, queryEngine, logger, clock, name,
                parentObjectId, systemPrompt, instruction, freshContext, tokenBudget,
                goalId, graphNodeId, sessionId, contextManager),
        };
    }
}
