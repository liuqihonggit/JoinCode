
namespace Core.Agents.Coordinator;

/// <summary>
/// 协调者智能体 — 主智能体，Role 固定为 Coordinator
/// 持有完整 ChatContextManager，管理用户对话的主 Agent
/// 继承 AgentBase 自动获得对话循环 + 压缩管线
/// </summary>
public sealed class CoordinatorAgent : AgentBase
{
    public CoordinatorAgent(string task, SubAgentOptions? options, IQueryEngine queryEngine, ILogger? logger,
        IClockService? clock = null, string? name = null, ObjectId? parentObjectId = default,
        string? systemPrompt = null, string? instruction = null, bool freshContext = false,
        int? tokenBudget = null, string? goalId = null, string? graphNodeId = null,
        ObjectId sessionId = default, IChatContextManager? contextManager = null)
        : base(task, options, queryEngine, logger, clock, name, AgentRole.Coordinator, null, parentObjectId,
            systemPrompt, instruction, freshContext, tokenBudget, goalId, graphNodeId, sessionId, contextManager)
    {
    }
}
