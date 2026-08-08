
namespace Core.Agents.Coordinator;

/// <summary>
/// 协作队友执行者 — 多智能体协作中的队友角色
/// </summary>
public sealed class TeammateAgent : ExecutorAgent
{
    public TeammateAgent(string task, SubAgentOptions? options, IQueryEngine queryEngine, ILogger? logger,
        IClockService? clock = null, string? name = null, ObjectId? parentObjectId = default,
        string? systemPrompt = null, string? instruction = null, bool freshContext = false,
        int? tokenBudget = null, string? goalId = null, string? graphNodeId = null,
        ObjectId sessionId = default, IChatContextManager? contextManager = null)
        : base(task, options, queryEngine, logger, ExecutorVariant.Teammate, clock, name, parentObjectId,
            systemPrompt, instruction, freshContext, tokenBudget, goalId, graphNodeId, sessionId, contextManager)
    {
    }
}
