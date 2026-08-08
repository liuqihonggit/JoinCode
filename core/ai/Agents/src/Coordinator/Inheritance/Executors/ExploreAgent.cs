
namespace Core.Agents.Coordinator;

/// <summary>
/// 探索执行者 — 快速代码库探索（只读，一次性）
/// </summary>
public sealed class ExploreAgent : ExecutorAgent
{
    public ExploreAgent(string task, SubAgentOptions? options, IQueryEngine queryEngine, ILogger? logger,
        IClockService? clock = null, string? name = null, ObjectId? parentObjectId = default,
        string? systemPrompt = null, string? instruction = null, bool freshContext = false,
        int? tokenBudget = null, string? goalId = null, string? graphNodeId = null,
        ObjectId sessionId = default, IChatContextManager? contextManager = null)
        : base(task, options, queryEngine, logger, ExecutorVariant.Explore, clock, name, parentObjectId,
            systemPrompt, instruction, freshContext, tokenBudget, goalId, graphNodeId, sessionId, contextManager)
    {
    }
}
