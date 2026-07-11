
namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 生命周期管理器 - 负责 Agent 的生成、状态管理和资源释放
/// </summary>
[Register]
public sealed partial class AgentLifecycleManager : IAgentLifecycleManager
{
    private readonly IQueryEngine _queryEngine;
    private readonly ILogger? _logger;
    private readonly AgentStateMachine _stateMachine;
    private readonly ConcurrentDictionary<string, SubAgent> _subAgents;
    private readonly ConcurrentDictionary<string, SubAgentResult> _results;
    private int _agentCounter;

    internal AgentStateMachine StateMachine => _stateMachine;

    public AgentLifecycleManager(IQueryEngine queryEngine,  AgentStateMachine stateMachine, ILogger? logger = null)
    {
        _queryEngine = queryEngine ?? throw new ArgumentNullException(nameof(queryEngine));
        _logger = logger;
        _stateMachine = stateMachine;
        _subAgents = new ConcurrentDictionary<string, SubAgent>();
        _results = new ConcurrentDictionary<string, SubAgentResult>();
    }

    /// <summary>
    /// 生成子Agent
    /// </summary>
    public Task<ISubAgent> SpawnSubAgentAsync(string task, SubAgentOptions? options = null, CancellationToken cancellationToken = default)
    {
        var agentId = GenerateAgentId();
        var agent = new SubAgent(agentId, task, options, _queryEngine, _logger);

        _subAgents[agentId] = agent;
        _stateMachine.RegisterAgent(agentId, task, options);

        _logger?.LogInformation("[AgentLifecycleManager] 生成子Agent {AgentId}: {Task}", agentId, task);

        return Task.FromResult<ISubAgent>(agent);
    }

    /// <summary>
    /// 批量生成子Agent
    /// </summary>
    public async Task<IReadOnlyList<ISubAgent>> SpawnSubAgentsAsync(IEnumerable<string> tasks, SubAgentOptions? options = null, CancellationToken cancellationToken = default)
    {
        var taskList = tasks.ToList();
        var spawnTasks = taskList
            .Select(task => SpawnSubAgentAsync(task, options, cancellationToken))
            .ToList();

        var agents = await Task.WhenAll(spawnTasks).ConfigureAwait(false);
        return agents.ToList();
    }

    /// <summary>
    /// 执行单个Agent
    /// </summary>
    public async Task<SubAgentResult> ExecuteAsync(ISubAgent agent, CancellationToken cancellationToken = default)
    {
        if (!await _stateMachine.TryTransitionAsync(agent.Id, TaskExecutionStatus.Running, "开始执行", cancellationToken))
        {
            return CreateErrorResult(agent.Id, "Agent状态不允许执行");
        }

        try
        {
            _logger?.LogInformation("[AgentLifecycleManager] 开始执行Agent {AgentId}", agent.Id);

            var result = await agent.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            _results[agent.Id] = result;

            var finalState = result.IsSuccess ? TaskExecutionStatus.Completed : TaskExecutionStatus.Failed;
            await _stateMachine.TryTransitionAsync(agent.Id, finalState, result.Error, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("[AgentLifecycleManager] Agent {AgentId} 执行完成，状态: {State}",
                agent.Id, finalState);

            return result;
        }
        catch (OperationCanceledException)
        {
            await _stateMachine.TryTransitionAsync(agent.Id, TaskExecutionStatus.Cancelled, "任务被取消", cancellationToken).ConfigureAwait(false);
            return CreateErrorResult(agent.Id, "任务被取消");
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[AgentLifecycleManager] Agent {AgentId} 执行失败", agent.Id);
            await _stateMachine.TryTransitionAsync(agent.Id, TaskExecutionStatus.Failed, ex.Message, cancellationToken).ConfigureAwait(false);
            return CreateErrorResult(agent.Id, ex.Message);
        }
    }

    /// <summary>
    /// 暂停Agent
    /// </summary>
    public async Task<bool> PauseAgentAsync(string agentId, CancellationToken ct = default)
    {
        if (_subAgents.TryGetValue(agentId, out var agent))
        {
            agent.Pause();
            return await _stateMachine.TryTransitionAsync(agentId, TaskExecutionStatus.Paused, "用户暂停", ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>
    /// 恢复Agent
    /// </summary>
    public async Task<bool> ResumeAgentAsync(string agentId, CancellationToken ct = default)
    {
        if (_subAgents.TryGetValue(agentId, out var agent))
        {
            agent.Resume();
            return await _stateMachine.TryTransitionAsync(agentId, TaskExecutionStatus.Running, "用户恢复", ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>
    /// 取消Agent
    /// </summary>
    public async Task<bool> CancelAgentAsync(string agentId, CancellationToken ct = default)
    {
        if (_subAgents.TryGetValue(agentId, out var agent))
        {
            agent.Cancel();
            return await _stateMachine.TryTransitionAsync(agentId, TaskExecutionStatus.Cancelled, "用户取消", ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>
    /// 取消所有Agent
    /// </summary>
    public async Task CancelAllAsync(CancellationToken ct = default)
    {
        foreach (var agent in _subAgents.Values)
        {
            agent.Cancel();
        }

        await Task.WhenAll(_subAgents.Values.Select(agent =>
            _stateMachine.TryTransitionAsync(agent.Id, TaskExecutionStatus.Cancelled, "批量取消", ct).AsTask())).ConfigureAwait(false);

        _logger?.LogInformation("[AgentLifecycleManager] 已取消所有Agent");
    }

    /// <summary>
    /// 重试失败的Agent
    /// </summary>
    public async Task<SubAgentResult?> RetryAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (!_subAgents.TryGetValue(agentId, out var agent))
        {
            return null;
        }

        var state = _stateMachine.GetState(agentId);
        if (state != TaskExecutionStatus.Failed && state != TaskExecutionStatus.Completed)
        {
            _logger?.LogWarning("[AgentLifecycleManager] Agent {AgentId} 状态 {State} 不允许重试", agentId, state);
            return null;
        }

        agent.Reset();
        await _stateMachine.TryTransitionAsync(agentId, TaskExecutionStatus.Pending, "准备重试", cancellationToken).ConfigureAwait(false);

        return await ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 释放Agent资源
    /// </summary>
    public Task DisposeAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        if (_subAgents.TryRemove(agentId, out var agent))
        {
            agent.Dispose();
        }
        _results.TryRemove(agentId, out _);
        _stateMachine.RemoveAgent(agentId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取Agent
    /// </summary>
    public Task<ISubAgent?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<ISubAgent?>(_subAgents.GetValueOrDefault(agentId));
    }

    /// <summary>
    /// 获取所有Agent
    /// </summary>
    public Task<IReadOnlyCollection<ISubAgent>> GetAllAgentsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyCollection<ISubAgent>>(_subAgents.Values.ToList());
    }

    /// <summary>
    /// 获取Agent结果
    /// </summary>
    public Task<SubAgentResult?> GetResultAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_results.GetValueOrDefault(agentId));
    }

    /// <summary>
    /// 获取所有结果
    /// </summary>
    public Task<IReadOnlyDictionary<string, SubAgentResult>> GetAllResultsAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyDictionary<string, SubAgentResult>>(
            _results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
    }

    /// <summary>
    /// 等待所有Agent完成
    /// </summary>
    public async Task WaitAllAsync(CancellationToken cancellationToken = default)
    {
        await _stateMachine.WaitAllFinalAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取Agent状态报告
    /// </summary>
    public Task<AgentStateReport> GetStateReportAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_stateMachine.GetReport());
    }

    /// <summary>
    /// 获取正在运行的Agent列表
    /// </summary>
    public Task<IReadOnlyList<RunningAgentInfo>> GetRunningAgentsAsync(CancellationToken cancellationToken = default)
    {
        var result = _subAgents.Values
            .Where(a => a.State == TaskExecutionStatus.Running)
            .Select(a => new RunningAgentInfo
            {
                Id = a.Id,
                Description = a.Task,
                AgentType = a.Options.AgentType,
                StartedAt = a.StartedAt
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<RunningAgentInfo>>(result);
    }

    private string GenerateAgentId()
    {
        var counter = Interlocked.Increment(ref _agentCounter);
        return $"agent-{counter:D4}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    private static SubAgentResult CreateErrorResult(string agentId, string error)
    {
        return new SubAgentResult
        {
            AgentId = agentId,
            IsSuccess = false,
            Output = string.Empty,
            Error = error
        };
    }
}
