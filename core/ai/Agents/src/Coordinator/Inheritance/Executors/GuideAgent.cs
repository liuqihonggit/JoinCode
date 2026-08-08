
namespace Core.Agents.Coordinator;

/// <summary>
/// 引导执行者 — 提供使用指导和最佳实践
/// </summary>
public sealed class GuideAgent : ExecutorAgent
{
    public GuideAgent(string task, SubAgentOptions? options, IQueryEngine queryEngine, ILogger? logger,
        IClockService? clock = null, string? name = null, ObjectId? parentObjectId = default,
        string? systemPrompt = null, string? instruction = null, bool freshContext = false,
        int? tokenBudget = null, string? goalId = null, string? graphNodeId = null,
        ObjectId sessionId = default, IChatContextManager? contextManager = null)
        : base(task, options, queryEngine, logger, ExecutorVariant.ClaudeCodeGuide, clock, name, parentObjectId,
            systemPrompt, instruction, freshContext, tokenBudget, goalId, graphNodeId, sessionId, contextManager)
    {
    }
}
