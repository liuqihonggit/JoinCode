
namespace Core.Agents.Coordinator;

/// <summary>
/// 代码执行者 — 代码读写编辑
/// </summary>
public sealed class CodeAgent : ExecutorAgent
{
    public CodeAgent(string task, SubAgentOptions? options, IQueryEngine queryEngine, ILogger? logger,
        IClockService? clock = null, string? name = null, ObjectId? parentObjectId = default,
        string? systemPrompt = null, string? instruction = null, bool freshContext = false,
        int? tokenBudget = null, string? goalId = null, string? graphNodeId = null,
        ObjectId sessionId = default, IChatContextManager? contextManager = null)
        : base(task, options, queryEngine, logger, ExecutorVariant.Code, clock, name, parentObjectId,
            systemPrompt, instruction, freshContext, tokenBudget, goalId, graphNodeId, sessionId, contextManager)
    {
    }
}
