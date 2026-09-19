
namespace Core.Agents.Coordinator;

/// <summary>
/// Agent 生命周期管理器 - 负责 Agent 的生成、状态管理和资源释放
/// </summary>
[Register(typeof(IAgentLifecycleManager), ServiceLifetime.Singleton)]
public sealed partial class AgentLifecycleManager : ServiceEntity, IAgentLifecycleManager {
    private readonly IQueryEngine _queryEngine;
    private readonly ILogger? _logger;
    private readonly AgentStateMachine _stateMachine;
    private readonly ConcurrentDictionary<string, AgentBase> _subAgents;
    private readonly ConcurrentDictionary<string, SubAgentResult> _results;
    private readonly SubAgentLivenessOptions? _livenessOptions;
    private readonly SubAgentPool? _agentPool;
    private int _agentCounter;

    /// <summary>暴露给同程序集内部使用的状态机引用，用于直接查询或驱动状态转换</summary>
    internal AgentStateMachine StateMachine => _stateMachine;

    /// <summary>暴露给同程序集内部使用的卡死防护配置，供 Scanner/Activator 等同程序集组件读取</summary>
    internal SubAgentLivenessOptions? LivenessOptions => _livenessOptions;

    /// <summary>暴露给同程序集内部使用的代理池，供 Coordinator 抢塞新任务</summary>
    internal SubAgentPool? AgentPool => _agentPool;

    /// <summary>
    /// 构造 Agent 生命周期管理器实例
    /// </summary>
    /// <param name="queryEngine">查询引擎，用于创建子代理</param>
    /// <param name="stateMachine">Agent 状态机，负责跟踪各 Agent 的执行状态</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="livenessOptions">卡死防护配置（L1 超时 + L2 检测参数），null=使用默认值</param>
    /// <param name="agentPool">代理池（L3 抢塞），null=完成后直接 Dispose</param>
    public AgentLifecycleManager(
        IQueryEngine queryEngine,
        AgentStateMachine stateMachine,
        ILogger? logger = null,
        SubAgentLivenessOptions? livenessOptions = null,
        SubAgentPool? agentPool = null) {
        _queryEngine = queryEngine ?? throw new ArgumentNullException(nameof(queryEngine));
        _logger = logger;
        _stateMachine = stateMachine;
        _livenessOptions = livenessOptions;
        _agentPool = agentPool;
        _subAgents = new ConcurrentDictionary<string, AgentBase>();
        _results = new ConcurrentDictionary<string, SubAgentResult>();
    }

    /// <summary>
    /// 生成子Agent
    /// </summary>
    public Task<IAgent> SpawnSubAgentAsync(string task, SubAgentOptions? options = null, CancellationToken cancellationToken = default, string? parentSessionId = null) {
        string? customUniqueId = null;
        if (!string.IsNullOrEmpty(parentSessionId)) {
            var counter = Interlocked.Increment(ref _agentCounter);
            customUniqueId = $"{parentSessionId}-sub-{counter:D2}";
        }
        var agent = AgentFactory.Create(
            task,
            options,
            _queryEngine,
            _logger,
            name: options?.DisplayName,
            role: options?.Role ?? AgentRole.Executor,
            variant: options?.Variant,
            systemPrompt: options?.SystemPrompt,
            freshContext: options?.FreshContext ?? false,
            tokenBudget: options?.TokenBudget,
            goalId: options?.GoalId,
            graphNodeId: options?.GraphNodeId,
            customUniqueId: customUniqueId);
        var agentId = agent.ObjectId.UniqueId;

        _subAgents[agentId] = agent;
        _stateMachine.RegisterAgent(agentId, task, options);

        _logger?.LogInformation("[AgentLifecycleManager] 生成子Agent {AgentId}: {Task}", agentId, task);

        return Task.FromResult<IAgent>(agent);
    }

    /// <summary>
    /// 批量生成子Agent
    /// </summary>
    public async Task<IReadOnlyList<IAgent>> SpawnSubAgentsAsync(IEnumerable<string> tasks, SubAgentOptions? options = null, CancellationToken cancellationToken = default) {
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
    public async Task<SubAgentResult> ExecuteAsync(IAgent agent, CancellationToken cancellationToken = default) {
        if (!await _stateMachine.TryTransitionAsync(agent.ObjectId.UniqueId, TaskExecutionStatus.Running, "开始执行", cancellationToken)) {
            return CreateErrorResult(agent.ObjectId.UniqueId, "Agent状态不允许执行");
        }

        try {
            _logger?.LogInformation("[AgentLifecycleManager] 开始执行Agent {AgentId}", agent.ObjectId.UniqueId);

            var timeoutSeconds = _livenessOptions?.AgentTimeoutSeconds ?? 0;
            if (timeoutSeconds > 0)
                _logger?.LogDebug("[AgentLifecycleManager] Agent {AgentId} 启用 L1 超时保护: {Timeout}s",
                    agent.ObjectId.UniqueId, timeoutSeconds);
            var result = timeoutSeconds > 0
                ? await ExecuteWithTimeoutAsync(agent, timeoutSeconds, cancellationToken).ConfigureAwait(false)
                : await agent.ExecuteAsync(cancellationToken).ConfigureAwait(false);

            _results[agent.ObjectId.UniqueId] = result;

            var finalState = result.IsSuccess ? TaskExecutionStatus.Completed : TaskExecutionStatus.Failed;
            await _stateMachine.TryTransitionAsync(agent.ObjectId.UniqueId, finalState, result.Error, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("[AgentLifecycleManager] Agent {AgentId} 执行完成，状态: {State}",
                agent.ObjectId.UniqueId, finalState);

            return result;
        } catch (TimeoutException ex) {
            _logger?.LogWarning("[AgentLifecycleManager] Agent {AgentId} 执行超时: {Message}", agent.ObjectId.UniqueId, ex.Message);
            await _stateMachine.TryTransitionAsync(agent.ObjectId.UniqueId, TaskExecutionStatus.Cancelled, ex.Message, cancellationToken).ConfigureAwait(false);
            return CreateErrorResult(agent.ObjectId.UniqueId, ex.Message);
        } catch (OperationCanceledException) {
            await _stateMachine.TryTransitionAsync(agent.ObjectId.UniqueId, TaskExecutionStatus.Cancelled, "任务被取消", cancellationToken).ConfigureAwait(false);
            return CreateErrorResult(agent.ObjectId.UniqueId, "任务被取消");
        } catch (Exception ex) {
            _logger?.LogError(ex, "[AgentLifecycleManager] Agent {AgentId} 执行失败", agent.ObjectId.UniqueId);
            await _stateMachine.TryTransitionAsync(agent.ObjectId.UniqueId, TaskExecutionStatus.Failed, ex.Message, cancellationToken).ConfigureAwait(false);
            return CreateErrorResult(agent.ObjectId.UniqueId, ex.Message);
        }
    }

    /// <summary>
    /// 带超时的子代理执行 — L1 预防层（ADR 0106）
    /// </summary>
    private async Task<SubAgentResult> ExecuteWithTimeoutAsync(IAgent agent, int timeoutSeconds, CancellationToken ct) {
        return await TimeoutHelper.WithTimeoutAsync(
            token => agent.ExecuteAsync(token),
            TimeSpan.FromSeconds(timeoutSeconds),
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 暂停Agent
    /// </summary>
    public async Task<bool> PauseAgentAsync(string agentId, CancellationToken ct = default) {
        if (_subAgents.TryGetValue(agentId, out var agent)) {
            agent.Pause();
            return await _stateMachine.TryTransitionAsync(agentId, TaskExecutionStatus.Paused, "用户暂停", ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>
    /// 恢复Agent
    /// </summary>
    public async Task<bool> ResumeAgentAsync(string agentId, CancellationToken ct = default) {
        if (_subAgents.TryGetValue(agentId, out var agent)) {
            agent.Resume();
            return await _stateMachine.TryTransitionAsync(agentId, TaskExecutionStatus.Running, "用户恢复", ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>
    /// 取消Agent
    /// </summary>
    public async Task<bool> CancelAgentAsync(string agentId, CancellationToken ct = default) {
        if (_subAgents.TryGetValue(agentId, out var agent)) {
            agent.Cancel();
            return await _stateMachine.TryTransitionAsync(agentId, TaskExecutionStatus.Cancelled, "用户取消", ct).ConfigureAwait(false);
        }
        return false;
    }

    /// <summary>
    /// 取消所有Agent
    /// </summary>
    public async Task CancelAllAsync(CancellationToken ct = default) {
        foreach (var agent in _subAgents.Values) {
            agent.Cancel();
        }

        await Task.WhenAll(_subAgents.Values.Select(agent =>
            _stateMachine.TryTransitionAsync(agent.ObjectId.UniqueId, TaskExecutionStatus.Cancelled, "批量取消", ct).AsTask())).ConfigureAwait(false);

        _logger?.LogInformation("[AgentLifecycleManager] 已取消所有Agent");
    }

    /// <summary>
    /// 重试失败的Agent
    /// </summary>
    public async Task<SubAgentResult?> RetryAsync(string agentId, CancellationToken cancellationToken = default) {
        if (!_subAgents.TryGetValue(agentId, out var agent)) {
            return null;
        }

        var state = _stateMachine.GetState(agentId);
        if (state != TaskExecutionStatus.Failed && state != TaskExecutionStatus.Completed) {
            _logger?.LogWarning("[AgentLifecycleManager] Agent {AgentId} 状态 {State} 不允许重试", agentId, state);
            return null;
        }

        agent.Reset();
        await _stateMachine.TryTransitionAsync(agentId, TaskExecutionStatus.Pending, "准备重试", cancellationToken).ConfigureAwait(false);

        return await ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 释放Agent资源 — 如果有代理池且 agent 已完成，回池而非 Dispose（ADR 0106 L3 抢塞）
    /// </summary>
    public Task DisposeAgentAsync(string agentId, CancellationToken cancellationToken = default) {
        if (_subAgents.TryRemove(agentId, out var agent)) {
            // L3 抢塞：已完成/失败的 agent 回池等待复用，否则直接 Dispose
            if (_agentPool is not null && agent.Status is TaskExecutionStatus.Completed or TaskExecutionStatus.Failed) {
                if (_agentPool.Return(agent))
                    _logger?.LogDebug("[AgentLifecycleManager] Agent {AgentId} 回池等待复用", agentId);
                else
                    _logger?.LogDebug("[AgentLifecycleManager] Agent {AgentId} 回池失败（池满/禁用），已 Dispose", agentId);
            } else {
                _logger?.LogDebug("[AgentLifecycleManager] Agent {AgentId} 状态 {State}，直接 Dispose", agentId, agent.Status);
                agent.Dispose();
            }
        }
        _results.TryRemove(agentId, out _);
        _stateMachine.RemoveAgent(agentId);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 获取Agent
    /// </summary>
    public Task<IAgent?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default) {
        return Task.FromResult<IAgent?>(_subAgents.GetValueOrDefault(agentId));
    }

    /// <summary>
    /// 获取所有Agent
    /// </summary>
    public Task<IReadOnlyCollection<IAgent>> GetAllAgentsAsync(CancellationToken cancellationToken = default) {
        return Task.FromResult<IReadOnlyCollection<IAgent>>(_subAgents.Values.Cast<IAgent>().ToList());
    }

    /// <summary>
    /// 获取Agent结果
    /// </summary>
    public Task<SubAgentResult?> GetResultAsync(string agentId, CancellationToken cancellationToken = default) {
        return Task.FromResult(_results.GetValueOrDefault(agentId));
    }

    /// <summary>
    /// 获取所有结果
    /// </summary>
    public Task<IReadOnlyDictionary<string, SubAgentResult>> GetAllResultsAsync(CancellationToken cancellationToken = default) {
        return Task.FromResult<IReadOnlyDictionary<string, SubAgentResult>>(
            _results.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
    }

    /// <summary>
    /// 等待所有Agent完成
    /// </summary>
    public async Task WaitAllAsync(CancellationToken cancellationToken = default) {
        await _stateMachine.WaitAllFinalAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取Agent状态报告
    /// </summary>
    public Task<AgentStateReport> GetStateReportAsync(CancellationToken cancellationToken = default) {
        return Task.FromResult(_stateMachine.GetReport());
    }

    /// <summary>
    /// 获取正在运行的Agent列表
    /// </summary>
    public Task<IEnumerable<RunningAgentInfo>> GetRunningAgentsAsync(CancellationToken cancellationToken = default) {
        var result = _subAgents.Values
            .Where(a => a.State == TaskExecutionStatus.Running)
            .Select(a => new RunningAgentInfo {
                Id = a.ObjectId.UniqueId,
                Description = a.Task,
                Role = a.Options.Role,
                Variant = a.Options.Variant,
                StartedAt = a.StartedAt
            })
            .ToList();

        return Task.FromResult<IEnumerable<RunningAgentInfo>>(result);
    }

    private string GenerateAgentId() {
        var counter = Interlocked.Increment(ref _agentCounter);
        return $"agent-{counter:D4}-{Guid.NewGuid().ToString("N")[..8]}";
    }

    private static SubAgentResult CreateErrorResult(string agentId, string error) {
        return new SubAgentResult {
            AgentId = agentId,
            IsSuccess = false,
            Output = string.Empty,
            Error = error
        };
    }
}