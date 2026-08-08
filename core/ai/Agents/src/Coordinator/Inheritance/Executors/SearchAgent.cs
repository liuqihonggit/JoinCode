
namespace Core.Agents.Coordinator;

/// <summary>
/// 搜索执行者 — 代码搜索导航（只读）
/// </summary>
public sealed class SearchAgent : ExecutorAgent
{
    public SearchAgent(string task, SubAgentOptions? options, IQueryEngine queryEngine, ILogger? logger,
        IClockService? clock = null, string? name = null, ObjectId? parentObjectId = default,
        string? systemPrompt = null, string? instruction = null, bool freshContext = false,
        int? tokenBudget = null, string? goalId = null, string? graphNodeId = null,
        ObjectId sessionId = default, IChatContextManager? contextManager = null)
        : base(task, options, queryEngine, logger, ExecutorVariant.Search, clock, name, parentObjectId,
            systemPrompt, instruction, freshContext, tokenBudget, goalId, graphNodeId, sessionId, contextManager)
    {
    }
}
