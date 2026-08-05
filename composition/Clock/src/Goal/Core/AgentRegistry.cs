namespace Core.Goal;

using JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent 中央注册表实现 — 树形组织 mainAgent → subAgents[]
/// 核心路由：SubAgentMap[mainAgent.Id] 获取该主 Agent 下的所有子 Agent
/// </summary>
[Register(typeof(IAgentRegistry))]
public sealed class AgentRegistry : ServiceEntity, IAgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentDescriptor> _agents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Dictionary<string, AgentDescriptor>> _subAgentMap = new(StringComparer.Ordinal);
    private readonly ILogger<AgentRegistry>? _logger;

    public AgentRegistry(ILogger<AgentRegistry>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>
    /// 子 Agent 路由表 — key=mainAgent.Id, value=子 Agent 列表
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyList<AgentDescriptor>> SubAgentMap
        => _subAgentMap.ToFrozenDictionary(
            kvp => kvp.Key,
            kvp => (IReadOnlyList<AgentDescriptor>)kvp.Value.Values.ToList().AsReadOnly(),
            StringComparer.Ordinal);

    public AgentDescriptor Register(AgentDescriptor agent)
    {
        ArgumentNullException.ThrowIfNull(agent);

        if (!_agents.TryAdd(agent.Id, agent))
        {
            _logger?.LogWarning("[AgentRegistry] Agent 已存在，忽略重复注册: {AgentId}", agent.Id);
            return _agents[agent.Id];
        }

        if (agent.IsSubAgent && agent.ParentAgentId is not null)
        {
            _subAgentMap.AddOrUpdate(
                agent.ParentAgentId,
                _ => new Dictionary<string, AgentDescriptor> { [agent.Id] = agent },
                (_, dict) => { dict[agent.Id] = agent; return dict; });
        }

        _logger?.LogDebug("[AgentRegistry] 注册 Agent: {AgentId} ({Name}, IsSub={IsSub}, Parent={ParentId}, Goal={GoalId})",
            agent.Id, agent.Name, agent.IsSubAgent, agent.ParentAgentId, agent.GoalId);

        return agent;
    }

    public bool Unregister(string agentId)
    {
        if (!_agents.TryRemove(agentId, out var agent))
            return false;

        if (agent.IsSubAgent && agent.ParentAgentId is not null)
        {
            if (_subAgentMap.TryGetValue(agent.ParentAgentId, out var siblings))
            {
                lock (siblings)
                {
                    siblings.Remove(agentId);
                }
            }
        }
        else if (!agent.IsSubAgent)
        {
            if (_agents.Values.Where(a => a.IsSubAgent && a.ParentAgentId == agentId).ToList() is { Count: > 0 } orphans)
            {
                foreach (var orphan in orphans)
                {
                    _agents.TryRemove(orphan.Id, out _);
                }
            }

            _subAgentMap.TryRemove(agentId, out _);
        }

        _logger?.LogDebug("[AgentRegistry] 注销 Agent: {AgentId} ({Name})", agent.Id, agent.Name);
        return true;
    }

    public AgentDescriptor? Get(string agentId) => _agents.GetValueOrDefault(agentId);

    public IReadOnlyList<AgentDescriptor> GetMainAgents()
        => [.. _agents.Values.Where(a => !a.IsSubAgent)];

    public IReadOnlyList<AgentDescriptor> GetSubAgents(string mainAgentId)
        => _subAgentMap.TryGetValue(mainAgentId, out var dict) ? dict.Values.ToList() : [];

    public IReadOnlyList<AgentDescriptor> GetByGoalId(string goalId)
        => [.. _agents.Values.Where(a => a.GoalId == goalId)];

    public IReadOnlyList<AgentDescriptor> GetByStatus(AgentStatus status)
        => [.. _agents.Values.Where(a => a.Status == status)];

    public int Count => _agents.Count;

    public void Clear()
    {
        _agents.Clear();
        _subAgentMap.Clear();
        _logger?.LogDebug("[AgentRegistry] 清空所有注册");
    }
}
