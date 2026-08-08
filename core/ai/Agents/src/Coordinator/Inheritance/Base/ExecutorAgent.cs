
namespace Core.Agents.Coordinator;

/// <summary>
/// 执行者智能体基类 — Role 固定为 Executor，子类通过 Variant 区分专长
/// 继承 AgentBase 自动获得对话循环 + 压缩管线
/// 子类: CodeAgent, SearchAgent, ExploreAgent, PlanAgent, DoctorAgent, VerificationAgent, GuideAgent, ContextCompressionAgent, TeammateAgent
/// </summary>
public abstract class ExecutorAgent : AgentBase
{
    /// <summary>
    /// ExecutorAgent 构造函数 — Role 固定为 Executor，Variant 由子类指定
    /// </summary>
    protected ExecutorAgent(
        string task,
        SubAgentOptions? options,
        IQueryEngine queryEngine,
        ILogger? logger,
        ExecutorVariant variant,
        IClockService? clock = null,
        string? name = null,
        ObjectId? parentObjectId = default,
        string? systemPrompt = null,
        string? instruction = null,
        bool freshContext = false,
        int? tokenBudget = null,
        string? goalId = null,
        string? graphNodeId = null,
        ObjectId sessionId = default,
        IChatContextManager? contextManager = null)
        : base(task, options, queryEngine, logger, clock, name, AgentRole.Executor, variant, parentObjectId, systemPrompt, instruction, freshContext, tokenBudget, goalId, graphNodeId, sessionId, contextManager)
    {
    }
}
