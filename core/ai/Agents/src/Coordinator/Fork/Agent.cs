
namespace Core.Agents.Coordinator;

/// <summary>
/// 统一 Agent — 派生自 AgentBase，过渡兼容
/// mainAgent 和 subAgent 共用此类
/// 静态查询方法（GetMainAgents/GetById/...）保留在此，因为返回类型是 Agent
/// </summary>
public sealed class Agent : AgentBase
{
    public Agent(
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
        ObjectId sessionId = default)
        : base(task, options, queryEngine, logger, clock, name, role, variant, parentObjectId, systemPrompt, instruction, freshContext, tokenBudget, goalId, graphNodeId, sessionId)
    {
    }

    /// <summary>
    /// 生成唯一 Agent Id
    /// </summary>
    public static string GenerateId() => $"agent-{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// 获取当前会话作用域 — 通过 SessionContext.AsyncLocal 隐式定位
    /// </summary>
    private static SessionScope? GetCurrentScope()
    {
        var sessionId = SessionContext.Current;
        if (sessionId is null) return null;
        return SessionRouter.GetScope(sessionId.Value);
    }

    /// <summary>
    /// 获取当前会话的所有主 Agent (Role=Coordinator) — 替代 AgentRegistry.GetMainAgents
    /// </summary>
    public static IReadOnlyList<Agent> GetMainAgents()
    {
        var scope = GetCurrentScope();
        if (scope is null) return [];
        return scope.GetAll<Agent>().Where(a => a.Role == AgentRole.Coordinator).ToList();
    }

    /// <summary>
    /// 按 ObjectId 获取 Agent — 仅在当前会话作用域内查找, 替代 AgentRegistry.Get
    /// </summary>
    public static Agent? GetById(ObjectId id)
    {
        var scope = GetCurrentScope();
        return scope?.Resolve<Agent>(id);
    }

    /// <summary>
    /// 获取指定主 Agent 的所有子 Agent — 通过 ParentObjectId 过滤, 替代 AgentRegistry.GetSubAgents
    /// </summary>
    public static IReadOnlyList<Agent> GetSubAgents(ObjectId mainAgentId)
    {
        var scope = GetCurrentScope();
        if (scope is null) return [];
        return scope.GetAll<Agent>().Where(a => a.ParentObjectId == mainAgentId).ToList();
    }

    /// <summary>
    /// 按 GoalId 获取 Agent — 替代 AgentRegistry.GetByGoalId
    /// </summary>
    public static IReadOnlyList<Agent> GetByGoalId(string goalId)
    {
        var scope = GetCurrentScope();
        if (scope is null) return [];
        return scope.GetAll<Agent>().Where(a => a.GoalId == goalId).ToList();
    }

    /// <summary>
    /// 按状态获取 Agent — 替代 AgentRegistry.GetByStatus
    /// </summary>
    public static IReadOnlyList<Agent> GetByStatus(TaskExecutionStatus status)
    {
        var scope = GetCurrentScope();
        if (scope is null) return [];
        return scope.GetAll<Agent>().Where(a => a.Status == status).ToList();
    }

    /// <summary>
    /// 暂停当前会话的指定主 Agent 的所有子 Agent
    /// </summary>
    public static void PauseAll(ObjectId mainAgentId)
    {
        foreach (var agent in GetSubAgents(mainAgentId))
            agent.Pause();
    }

    /// <summary>
    /// 恢复当前会话的指定主 Agent 的所有子 Agent
    /// </summary>
    public static void ResumeAll(ObjectId mainAgentId)
    {
        foreach (var agent in GetSubAgents(mainAgentId))
            agent.Resume();
    }

    /// <summary>
    /// 取消当前会话的指定主 Agent 的所有子 Agent
    /// </summary>
    public static void CancelAll(ObjectId mainAgentId)
    {
        foreach (var agent in GetSubAgents(mainAgentId))
            agent.Cancel();
    }

    /// <summary>
    /// 暂停所有会话的所有 Agent — 跨会话操作, 替代 AgentRegistry.PauseGlobal
    /// </summary>
    public static void PauseGlobal()
    {
        foreach (var scope in SessionRouter.GetAllScopes())
            foreach (var agent in scope.GetAll<Agent>())
                agent.Pause();
    }

    /// <summary>
    /// 恢复所有会话的所有 Agent — 跨会话操作, 替代 AgentRegistry.ResumeGlobal
    /// </summary>
    public static void ResumeGlobal()
    {
        foreach (var scope in SessionRouter.GetAllScopes())
            foreach (var agent in scope.GetAll<Agent>())
                agent.Resume();
    }
}
