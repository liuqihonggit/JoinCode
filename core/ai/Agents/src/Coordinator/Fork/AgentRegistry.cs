namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 注册器 — 独立类，管理所有 Agent 的注册/查询/LLM循环控制
/// 全局唯一实例通过 Agent.Registry 静态属性获取
/// </summary>
public sealed class AgentRegistry
{
    private readonly ConcurrentDictionary<ObjectId, Agent> _agents = new();
    private readonly ConcurrentDictionary<ObjectId, List<ObjectId>> _subAgentMap = new();
    private readonly ILogger? _logger;

    public AgentRegistry(ILogger? logger = null)
    {
        _logger = logger;
    }

    internal void Add(ObjectId id, Agent agent)
    {
        if (!_agents.TryAdd(id, agent))
            return;

        if (agent.IsSubAgent && agent.ParentObjectId is not null)
        {
            _subAgentMap.AddOrUpdate(
                agent.ParentObjectId.Value,
                _ => [id],
                (_, list) => { lock (list) { list.Add(id); } return list; });
        }

        _logger?.LogDebug("[AgentRegistry] 注册 Agent: {AgentId} ({Name}, IsSub={IsSub})", id, agent.Name, agent.IsSubAgent);
    }

    internal bool Remove(ObjectId id)
    {
        if (!_agents.TryRemove(id, out var agent))
            return false;

        if (agent.IsSubAgent && agent.ParentObjectId is not null)
        {
            if (_subAgentMap.TryGetValue(agent.ParentObjectId.Value, out var siblings))
            {
                lock (siblings)
                {
                    siblings.Remove(id);
                }
            }
        }
        else if (!agent.IsSubAgent)
        {
            _subAgentMap.TryRemove(id, out _);
        }

        _logger?.LogDebug("[AgentRegistry] 移除 Agent: {AgentId}", id);
        return true;
    }

    public Agent? Get(ObjectId id) => _agents.GetValueOrDefault(id);

    public IReadOnlyList<Agent> GetMainAgents()
        => [.. _agents.Values.Where(a => !a.IsSubAgent)];

    public IReadOnlyList<Agent> GetSubAgents(ObjectId mainAgentId)
        => _subAgentMap.TryGetValue(mainAgentId, out var ids)
            ? ids.Select(id => _agents.GetValueOrDefault(id)).Where(a => a is not null).Cast<Agent>().ToList()
            : [];

    public IReadOnlyList<Agent> GetByGoalId(string goalId)
        => [.. _agents.Values.Where(a => a.GoalId == goalId)];

    public IReadOnlyList<Agent> GetByStatus(TaskExecutionStatus status)
        => [.. _agents.Values.Where(a => a.Status == status)];

    public IReadOnlyDictionary<ObjectId, IReadOnlyList<ObjectId>> SubAgentMap
        => _subAgentMap.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<ObjectId>)kvp.Value.AsReadOnly());

    public int Count => _agents.Count;

    public void Clear()
    {
        _agents.Clear();
        _subAgentMap.Clear();
    }

    public void PauseAll(ObjectId mainAgentId)
    {
        foreach (var agent in GetSubAgents(mainAgentId))
        {
            agent.Pause();
        }
    }

    public void ResumeAll(ObjectId mainAgentId)
    {
        foreach (var agent in GetSubAgents(mainAgentId))
        {
            agent.Resume();
        }
    }

    public void CancelAll(ObjectId mainAgentId)
    {
        foreach (var agent in GetSubAgents(mainAgentId))
        {
            agent.Cancel();
        }
    }

    public void PauseGlobal()
    {
        foreach (var agent in _agents.Values)
        {
            agent.Pause();
        }
    }

    public void ResumeGlobal()
    {
        foreach (var agent in _agents.Values)
        {
            agent.Resume();
        }
    }
}
