namespace Core.Goal;

using JoinCode.Abstractions.Models.Agent;

/// <summary>
/// Agent 中央注册表实现 — 树形组织 mainAgent → subAgents[]
/// 核心路由：SubAgentMap[mainAgent.Id] 获取该主 Agent 下的所有子 Agent
/// 支持批量 LLM 循环控制（PauseAll/ResumeAll/CancelAll）
/// </summary>
[Register(typeof(IAgentRegistry))]
public sealed class AgentRegistry : IAgentRegistry
{
    private readonly ConcurrentDictionary<string, AgentDescriptor> _agents = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<AgentDescriptor>> _subAgentMap = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ISubAgent> _liveAgents = new(StringComparer.Ordinal);
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
            kvp => (IReadOnlyList<AgentDescriptor>)kvp.Value.AsReadOnly(),
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
                _ => [agent],
                (_, list) => { list.Add(agent); return list; });
        }

        _logger?.LogDebug("[AgentRegistry] 注册 Agent: {AgentId} ({Name}, IsSub={IsSub}, Parent={ParentId}, Goal={GoalId})",
            agent.Id, agent.Name, agent.IsSubAgent, agent.ParentAgentId, agent.GoalId);

        return agent;
    }

    /// <summary>
    /// 注册 Agent 并关联运行时实例（用于 LLM 循环控制）
    /// </summary>
    public AgentDescriptor Register(AgentDescriptor descriptor, ISubAgent liveAgent)
    {
        var result = Register(descriptor);
        if (liveAgent is not null)
        {
            _liveAgents[descriptor.Id] = liveAgent;
        }
        return result;
    }

    /// <summary>
    /// 获取运行时 Agent 实例（用于 LLM 循环控制）
    /// </summary>
    public ISubAgent? GetLiveAgent(string agentId) => _liveAgents.GetValueOrDefault(agentId);

    public bool Unregister(string agentId)
    {
        if (!_agents.TryRemove(agentId, out var agent))
            return false;

        _liveAgents.TryRemove(agentId, out _);

        if (agent.IsSubAgent && agent.ParentAgentId is not null)
        {
            if (_subAgentMap.TryGetValue(agent.ParentAgentId, out var siblings))
            {
                lock (siblings)
                {
                    siblings.RemoveAll(a => a.Id == agentId);
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
                    _liveAgents.TryRemove(orphan.Id, out _);
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
        => _subAgentMap.TryGetValue(mainAgentId, out var list) ? list.AsReadOnly() : [];

    public IReadOnlyList<AgentDescriptor> GetByGoalId(string goalId)
        => [.. _agents.Values.Where(a => a.GoalId == goalId)];

    public IReadOnlyList<AgentDescriptor> GetByStatus(AgentStatus status)
        => [.. _agents.Values.Where(a => a.Status == status)];

    public int Count => _agents.Count;

    public void Clear()
    {
        _agents.Clear();
        _subAgentMap.Clear();
        _liveAgents.Clear();
        _logger?.LogDebug("[AgentRegistry] 清空所有注册");
    }

    /// <summary>
    /// 暂停指定 mainAgent 下所有 subAgent 的 LLM 循环
    /// </summary>
    public void PauseAll(string mainAgentId)
    {
        var subAgents = GetSubAgents(mainAgentId);
        foreach (var desc in subAgents)
        {
            if (_liveAgents.TryGetValue(desc.Id, out var live))
            {
                live.Pause();
                _logger?.LogDebug("[AgentRegistry] 暂停 Agent: {AgentId}", desc.Id);
            }
        }
    }

    /// <summary>
    /// 恢复指定 mainAgent 下所有 subAgent 的 LLM 循环
    /// </summary>
    public void ResumeAll(string mainAgentId)
    {
        var subAgents = GetSubAgents(mainAgentId);
        foreach (var desc in subAgents)
        {
            if (_liveAgents.TryGetValue(desc.Id, out var live))
            {
                live.Resume();
                _logger?.LogDebug("[AgentRegistry] 恢复 Agent: {AgentId}", desc.Id);
            }
        }
    }

    /// <summary>
    /// 取消指定 mainAgent 下所有 subAgent 的 LLM 循环
    /// </summary>
    public void CancelAll(string mainAgentId)
    {
        var subAgents = GetSubAgents(mainAgentId);
        foreach (var desc in subAgents)
        {
            if (_liveAgents.TryGetValue(desc.Id, out var live))
            {
                live.Cancel();
                _logger?.LogDebug("[AgentRegistry] 取消 Agent: {AgentId}", desc.Id);
            }
        }
    }

    /// <summary>
    /// 暂停全局所有 Agent 的 LLM 循环
    /// </summary>
    public void PauseGlobal()
    {
        foreach (var live in _liveAgents.Values)
        {
            live.Pause();
        }
        _logger?.LogDebug("[AgentRegistry] 全局暂停所有 Agent");
    }

    /// <summary>
    /// 恢复全局所有 Agent 的 LLM 循环
    /// </summary>
    public void ResumeGlobal()
    {
        foreach (var live in _liveAgents.Values)
        {
            live.Resume();
        }
        _logger?.LogDebug("[AgentRegistry] 全局恢复所有 Agent");
    }
}
