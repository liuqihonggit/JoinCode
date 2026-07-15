using JoinCode.Abstractions.Attributes;

namespace Core.Agents.Coordinator;

/// <summary>
/// Agent协调器 - 提供高级协调功能，包括重试策略、断路器模式、资源清理等
/// 职责：协调 IAgentLifecycleManager、IAgentWorktreeManager、IAgentMessageBroker、IAgentExecutionEngine
/// </summary>
[Register(typeof(IAgentCoordinator))]
[Register(typeof(ISubAgentCoordinator))]
[Register(typeof(ITeammateObserver))]
public sealed partial class AgentCoordinator : IAgentCoordinator, ISubAgentCoordinator, ITeammateObserver
{
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly IAgentWorktreeManager _worktreeManager;
    private readonly IAgentMessageBroker _messageBroker;
    private readonly IAgentExecutionEngine _executionEngine;
    private readonly IClockService _clock;
    [Inject] private readonly ILogger<AgentCoordinator>? _logger;
    [Inject] private readonly ISubAgentContextAccessor _subAgentContextAccessor;
    private readonly IForkSubAgentManager? _forkManager;
    private readonly ISwarmPermissionBridge? _permissionBridge;
    private readonly JoinCode.Abstractions.Interfaces.ITeammateReconnectService? _reconnectService;

    private readonly ConcurrentDictionary<string, AgentExecutionContext> _executionContexts;
    private readonly ConcurrentDictionary<string, DateTime> _agentStartTimes;
    private readonly MiddlewarePipeline<AgentDisposeContext> _disposePipeline;
    private readonly MiddlewarePipeline<AgentSpawnCoordContext> _spawnPipeline;

    public event EventHandler<AgentTaskStatusChangedEventArgs>? TaskStatusChanged;
    public event EventHandler<TeammateChangedEventArgs>? TeammateChanged;

    public AgentCoordinator(
        AgentCoreDependencies core,
        IClockService clock,
        MiddlewarePipeline<AgentDisposeContext> disposePipeline,
        MiddlewarePipeline<AgentSpawnCoordContext> spawnPipeline,
        AgentPermissionDependencies? permission = null,
        AgentTeamDependencies? team = null,
        IForkSubAgentManager? forkManager = null,
        ILogger<AgentCoordinator>? logger = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null)
    {
        _lifecycleManager = core.LifecycleManager ?? throw new ArgumentNullException(nameof(core.LifecycleManager));
        _worktreeManager = core.WorktreeManager ?? throw new ArgumentNullException(nameof(core.WorktreeManager));
        _messageBroker = core.MessageBroker ?? throw new ArgumentNullException(nameof(core.MessageBroker));
        _executionEngine = core.ExecutionEngine ?? throw new ArgumentNullException(nameof(core.ExecutionEngine));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _disposePipeline = disposePipeline ?? throw new ArgumentNullException(nameof(disposePipeline));
        _spawnPipeline = spawnPipeline ?? throw new ArgumentNullException(nameof(spawnPipeline));
        _logger = logger;
        _subAgentContextAccessor = subAgentContextAccessor ?? new SubAgentContextAccessor();
        _forkManager = forkManager;
        _permissionBridge = permission?.PermissionBridge;
        _reconnectService = team?.ReconnectService;
        _executionContexts = new ConcurrentDictionary<string, AgentExecutionContext>();
        _agentStartTimes = new ConcurrentDictionary<string, DateTime>();

        core.StateMachine.StateChanged += (_, e) =>
        {
            TaskStatusChanged?.Invoke(this, new AgentTaskStatusChangedEventArgs(e.AgentId, e.OldState, e.NewState));
            TeammateChanged?.Invoke(this, new TeammateChangedEventArgs
            {
                AgentId = e.AgentId,
                OldState = e.OldState.ToAgentStatus(),
                NewState = e.NewState.ToAgentStatus(),
            });
        };
    }

    #region Agent 生命周期管理（含协调逻辑）

    public async Task<ISubAgent> SpawnSubAgentAsync(string task, SubAgentOptions? options = null, CancellationToken cancellationToken = default)
    {
        var ctx = new AgentSpawnCoordContext
        {
            Task = task,
            Options = options,
            CancellationToken = cancellationToken,
        };
        await _spawnPipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);

        if (ctx.Agent is not null)
        {
            if (ctx.SpawnedAt != default)
            {
                _agentStartTimes[ctx.AgentId] = ctx.SpawnedAt;
            }
            if (ctx.ExecutionContext is not null)
            {
                _executionContexts[ctx.AgentId] = ctx.ExecutionContext;
            }
        }

        return ctx.Agent ?? throw new InvalidOperationException("Spawn pipeline completed without agent");
    }

    public async Task<IReadOnlyList<ISubAgent>> SpawnSubAgentsAsync(IEnumerable<string> tasks, SubAgentOptions? options = null, CancellationToken cancellationToken = default)
    {
        var taskList = tasks.ToList();
        var spawnTasks = taskList
            .Select(task => SpawnSubAgentAsync(task, options, cancellationToken))
            .ToList();

        var agents = await Task.WhenAll(spawnTasks).ConfigureAwait(false);
        return agents.ToList();
    }

    public async Task<SubAgentResult> ExecuteAsync(ISubAgent agent, CancellationToken cancellationToken = default)
    {
        if (!_executionContexts.TryGetValue(agent.Id, out var context))
        {
            context = new AgentExecutionContext
            {
                AgentId = agent.Id,
                Task = agent.Task,
                SpawnedAt = _clock.GetUtcNow(),
                RetryCount = 0
            };
            _executionContexts[agent.Id] = context;
        }

        context.LastExecutionStart = _clock.GetUtcNow();

        try
        {
            var result = await _lifecycleManager.ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);

            context.LastExecutionEnd = _clock.GetUtcNow();
            context.IsSuccess = result.IsSuccess;

            if (result.IsSuccess)
            {
                _logger?.LogInformation("[AgentCoordinator] Agent {AgentId} 执行成功", agent.Id);
            }
            else
            {
                _logger?.LogWarning("[AgentCoordinator] Agent {AgentId} 执行失败: {Error}", agent.Id, result.Error);
            }

            return result;
        }
        catch (Exception ex)
        {
            context.LastExecutionEnd = _clock.GetUtcNow();
            context.IsSuccess = false;
            _logger?.LogError(ex, "[AgentCoordinator] Agent {AgentId} 执行异常", agent.Id);
            throw;
        }
    }

    public Task<bool> PauseAgentAsync(string agentId, CancellationToken ct = default)
    {
        _logger?.LogInformation("[AgentCoordinator] 暂停Agent {AgentId}", agentId);
        return _lifecycleManager.PauseAgentAsync(agentId, ct);
    }

    public Task<bool> ResumeAgentAsync(string agentId, CancellationToken ct = default)
    {
        _logger?.LogInformation("[AgentCoordinator] 恢复Agent {AgentId}", agentId);
        return _lifecycleManager.ResumeAgentAsync(agentId, ct);
    }

    public async Task<bool> CancelAgentAsync(string agentId, CancellationToken ct = default)
    {
        _logger?.LogInformation("[AgentCoordinator] 取消Agent {AgentId}", agentId);

        if (_executionContexts.TryGetValue(agentId, out var context))
        {
            context.IsCancelled = true;
        }

        return await _lifecycleManager.CancelAgentAsync(agentId, ct).ConfigureAwait(false);
    }

    public async Task CancelAllAsync(CancellationToken ct = default)
    {
        _logger?.LogInformation("[AgentCoordinator] 取消所有Agent");

        foreach (var context in _executionContexts.Values)
        {
            context.IsCancelled = true;
        }

        await _lifecycleManager.CancelAllAsync(ct).ConfigureAwait(false);
    }

    public async Task<SubAgentResult?> RetryAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return await RetryWithPolicyAsync(agentId, RetryPolicy.Default, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 使用指定策略重试Agent
    /// </summary>
    public async Task<SubAgentResult?> RetryWithPolicyAsync(string agentId, RetryPolicy policy, CancellationToken cancellationToken = default)
    {
        if (!_executionContexts.TryGetValue(agentId, out var context))
        {
            _logger?.LogWarning("[AgentCoordinator] 无法找到Agent {AgentId} 的执行上下文", agentId);
            return null;
        }

        if (context.RetryCount >= policy.MaxRetries)
        {
            _logger?.LogWarning("[AgentCoordinator] Agent {AgentId} 已达到最大重试次数 {MaxRetries}", agentId, policy.MaxRetries);
            return null;
        }

        context.RetryCount++;
        var delay = policy.GetDelay(context.RetryCount);

        _logger?.LogInformation("[AgentCoordinator] 等待 {DelayMs}ms 后重试Agent {AgentId} (第{RetryCount}次)",
            delay.TotalMilliseconds, agentId, context.RetryCount);

        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

        var result = await _lifecycleManager.RetryAsync(agentId, cancellationToken).ConfigureAwait(false);

        if (result != null)
        {
            context.IsSuccess = result.IsSuccess;
            if (result.IsSuccess)
            {
                _logger?.LogInformation("[AgentCoordinator] Agent {AgentId} 重试成功", agentId);
            }
            else if (context.RetryCount < policy.MaxRetries)
            {
                _logger?.LogWarning("[AgentCoordinator] Agent {AgentId} 重试失败，还可重试 {Remaining} 次",
                    agentId, policy.MaxRetries - context.RetryCount);
            }
        }

        return result;
    }

    /// <summary>
    /// 执行Agent并在失败时自动重试
    /// </summary>
    public async Task<SubAgentResult> ExecuteWithRetryAsync(SubAgent agent, RetryPolicy? policy = null, CancellationToken cancellationToken = default)
    {
        policy ??= RetryPolicy.Default;
        var result = await ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);

        while (!result.IsSuccess && !cancellationToken.IsCancellationRequested)
        {
            if (!_executionContexts.TryGetValue(agent.Id, out var context))
            {
                break;
            }

            if (context.RetryCount >= policy.MaxRetries)
            {
                break;
            }

            var retryResult = await RetryWithPolicyAsync(agent.Id, policy, cancellationToken).ConfigureAwait(false);
            if (retryResult == null)
            {
                break;
            }

            result = retryResult;
        }

        return result;
    }

    public async Task DisposeAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        _logger?.LogInformation("[AgentCoordinator] 释放Agent {AgentId} 资源", agentId);

        var ctx = new AgentDisposeContext
        {
            AgentId = agentId,
            CancellationToken = cancellationToken,
        };
        await _disposePipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);

        _executionContexts.TryRemove(agentId, out _);
        _agentStartTimes.TryRemove(agentId, out _);
    }

    #endregion

    #region 执行策略（含协调逻辑）

    public async Task<IReadOnlyList<SubAgentResult>> ExecuteParallelAsync(
        IEnumerable<ISubAgent> agents,
        ParallelOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var agentList = agents.ToList();
        _logger?.LogInformation("[AgentCoordinator] 并行执行 {Count} 个Agent", agentList.Count);

        foreach (var agent in agentList)
        {
            if (_executionContexts.TryGetValue(agent.Id, out var context))
            {
                context.ExecutionMode = ExecutionMode.Parallel;
            }
        }

        return await _executionEngine.ExecuteParallelAsync(agentList, options, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SubAgentResult>> ExecuteSequentialAsync(
        IEnumerable<ISubAgent> agents,
        CancellationToken cancellationToken = default)
    {
        var agentList = agents.ToList();
        _logger?.LogInformation("[AgentCoordinator] 串行执行 {Count} 个Agent", agentList.Count);

        foreach (var agent in agentList)
        {
            if (_executionContexts.TryGetValue(agent.Id, out var context))
            {
                context.ExecutionMode = ExecutionMode.Sequential;
            }
        }

        return await _executionEngine.ExecuteSequentialAsync(agentList, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行主Agent，失败时依次尝试备用Agent
    /// </summary>
    public async Task<FallbackExecutionResult> ExecuteWithFallbackAsync(
        ISubAgent primaryAgent,
        IEnumerable<ISubAgent> fallbackAgents,
        CancellationToken cancellationToken = default)
    {
        var fallbacks = fallbackAgents.ToList();
        _logger?.LogInformation("[AgentCoordinator] 执行主Agent {AgentId}，准备 {FallbackCount} 个备用Agent",
            primaryAgent.Id, fallbacks.Count);

        var results = new List<SubAgentResult>();
        var primaryResult = await ExecuteAsync(primaryAgent, cancellationToken).ConfigureAwait(false);
        results.Add(primaryResult);

        if (primaryResult.IsSuccess)
        {
            return new FallbackExecutionResult
            {
                AllResults = results,
                SuccessfulResult = primaryResult,
                SuccessAgentId = primaryAgent.Id,
                AttemptCount = 1
            };
        }

        _logger?.LogWarning("[AgentCoordinator] 主Agent {AgentId} 失败，尝试备用Agent", primaryAgent.Id);

        foreach (var fallback in fallbacks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var fallbackResult = await ExecuteAsync(fallback, cancellationToken).ConfigureAwait(false);
            results.Add(fallbackResult);

            if (fallbackResult.IsSuccess)
            {
                _logger?.LogInformation("[AgentCoordinator] 备用Agent {AgentId} 执行成功", fallback.Id);
                return new FallbackExecutionResult
                {
                    AllResults = results,
                    SuccessfulResult = fallbackResult,
                    SuccessAgentId = fallback.Id,
                    AttemptCount = results.Count
                };
            }
        }

        _logger?.LogError("[AgentCoordinator] 所有备用Agent均失败");
        return new FallbackExecutionResult
        {
            AllResults = results,
            SuccessfulResult = null,
            SuccessAgentId = null,
            AttemptCount = results.Count
        };
    }

    #endregion

    #region 消息通信（含协调逻辑）

    public async Task<bool> SendMessageAsync(string agentId, CoordinatorAgentMessage message, CancellationToken cancellationToken = default)
    {
        var agent = await _lifecycleManager.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agent == null)
        {
            _logger?.LogWarning("[AgentCoordinator] 无法向不存在的Agent {AgentId} 发送消息", agentId);
            return false;
        }

        if (agent.State.IsTerminal())
        {
            _logger?.LogWarning("[AgentCoordinator] Agent {AgentId} 已结束，无法接收消息", agentId);
            return false;
        }

        return await _messageBroker.SendMessageAsync(agentId, message, cancellationToken).ConfigureAwait(false);
    }

    public async Task BroadcastAsync(CoordinatorAgentMessage message, CancellationToken cancellationToken = default)
    {
        var allAgents = await _lifecycleManager.GetAllAgentsAsync(cancellationToken).ConfigureAwait(false);
        var activeAgentCount = allAgents?.Count(a => !a.State.IsTerminal()) ?? 0;

        _logger?.LogInformation("[AgentCoordinator] 广播消息给 {Count} 个活跃Agent", activeAgentCount);

        await _messageBroker.BroadcastAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public IAsyncEnumerable<CoordinatorAgentMessage> ReadMessagesAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return _messageBroker.ReadMessagesAsync(agentId, cancellationToken);
    }

    #endregion

    #region 查询和报告（含协调逻辑）

    public async Task<SubAgentResult?> GetResultAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return await _lifecycleManager.GetResultAsync(agentId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<string, SubAgentResult>> GetAllResultsAsync(CancellationToken cancellationToken = default)
    {
        return await _lifecycleManager.GetAllResultsAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task WaitAllAsync(CancellationToken cancellationToken = default) => _lifecycleManager.WaitAllAsync(cancellationToken);

    public async Task<AgentStateReport> GetStateReportAsync(CancellationToken cancellationToken = default)
    {
        return await _lifecycleManager.GetStateReportAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<CoordinatorReport> GetCoordinatorReportAsync(CancellationToken cancellationToken = default)
    {
        var stateReport = await _lifecycleManager.GetStateReportAsync(cancellationToken).ConfigureAwait(false);
        var contexts = _executionContexts.Values.ToList();

        return new CoordinatorReport
        {
            TotalAgents = stateReport.TotalAgents,
            PendingCount = stateReport.PendingCount,
            RunningCount = stateReport.RunningCount,
            PausedCount = stateReport.PausedCount,
            CompletedCount = stateReport.CompletedCount,
            FailedCount = stateReport.FailedCount,
            CancelledCount = stateReport.CancelledCount,
            Agents = stateReport.Agents.Select(a => new AgentInfo
            {
                Id = a.AgentId,
                Task = a.Task,
                State = a.CurrentState,
                ExecutionTimeMs = a.ExecutionTimeMs
            }).ToList(),
            AverageExecutionTimeMs = CalculateAverageExecutionTime(contexts),
            TotalRetries = contexts.Sum(c => c.RetryCount),
            AgentsWithRetries = contexts.Count(c => c.RetryCount > 0)
        };
    }

    public Task<AgentWorktreeSession?> GetWorktreeSessionAsync(string agentId, CancellationToken cancellationToken = default)
    {
        return _worktreeManager.GetWorktreeSessionAsync(agentId, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, AgentWorktreeSession>> GetAllWorktreeSessionsAsync(CancellationToken cancellationToken = default)
    {
        return _worktreeManager.GetAllWorktreeSessionsAsync(cancellationToken);
    }

    /// <summary>
    /// 获取Agent的执行上下文
    /// </summary>
    public AgentExecutionContext? GetExecutionContext(string agentId)
    {
        _executionContexts.TryGetValue(agentId, out var context);
        return context;
    }

    /// <summary>
    /// 获取Agent的执行持续时间
    /// </summary>
    public TimeSpan? GetAgentExecutionDuration(string agentId)
    {
        if (!_agentStartTimes.TryGetValue(agentId, out var startTime))
        {
            return null;
        }

        if (_executionContexts.TryGetValue(agentId, out var context) && context.LastExecutionEnd.HasValue)
        {
            return context.LastExecutionEnd.Value - startTime;
        }

        return _clock.GetUtcNow() - startTime;
    }

    #endregion

    #region IAgentCoordinator 实现

    public async Task<bool> StopAgentAsync(string agentId, CancellationToken cancellationToken = default)
    {
        var agent = await _lifecycleManager.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agent == null)
        {
            _logger?.LogWarning("[AgentCoordinator] 无法停止不存在的Agent {AgentId}", agentId);
            return false;
        }

        if (agent.State != TaskExecutionStatus.Running && agent.State != TaskExecutionStatus.Pending)
        {
            _logger?.LogWarning("[AgentCoordinator] Agent {AgentId} 状态为 {State}，无法停止", agentId, agent.State);
            return false;
        }

        _logger?.LogInformation("[AgentCoordinator] 停止Agent {AgentId}", agentId);

        agent.CancellationTokenSource?.Cancel();
        agent.State = TaskExecutionStatus.Cancelled;

        if (_executionContexts.TryGetValue(agentId, out var context))
        {
            context.IsCancelled = true;
        }

        return await _lifecycleManager.CancelAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<RunningAgentInfo>> GetRunningAgentsAsync(CancellationToken cancellationToken = default)
    {
        return _lifecycleManager.GetRunningAgentsAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TeammateInfo>> GetRunningTeammatesAsync()
    {
        var report = await _lifecycleManager.GetStateReportAsync(CancellationToken.None).ConfigureAwait(false);
        return report.Agents
            .Where(a => a.CurrentState == TaskExecutionStatus.Running || a.CurrentState == TaskExecutionStatus.Paused)
            .Select(MapToTeammateInfo)
            .ToList();
    }

    private static TeammateInfo MapToTeammateInfo(AgentStateInfo info)
    {
        var options = info.Options;
        var progress = info.Progress;
        return new TeammateInfo
        {
            Id = info.AgentId,
            DisplayName = options?.DisplayName ?? info.AgentId,
            SpinnerVerb = options?.SpinnerVerb ?? "Working",
            ColorHex = options?.ColorHex ?? "#2587EB",
            State = info.CurrentState.ToAgentStatus(),
            StartedAt = info.StartedAt,
            TokenCount = progress?.TokenCount ?? 0,
            ToolUseCount = progress?.ToolUseCount ?? 0,
            LastActivity = progress?.LastActivity?.ActivityDescription ?? info.Task,
            RecentActivities = progress?.RecentActivities,
        };
    }

    #endregion

    #region 批量操作

    /// <summary>
    /// 批量释放所有Agent资源（并行执行）
    /// </summary>
    public async Task DisposeAllAgentsAsync(CancellationToken cancellationToken = default)
    {
        var agentIds = _executionContexts.Keys.ToList();

        _logger?.LogInformation("[AgentCoordinator] 批量释放 {Count} 个Agent资源", agentIds.Count);

        var disposeTasks = agentIds.Select(agentId => DisposeAgentAsync(agentId, cancellationToken));
        await Task.WhenAll(disposeTasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取执行统计信息
    /// </summary>
    public ExecutionStatistics GetExecutionStatistics()
    {
        var contexts = _executionContexts.Values.ToList();
        var completedContexts = contexts.Where(c => c.IsSuccess.HasValue).ToList();

        return new ExecutionStatistics
        {
            TotalAgents = contexts.Count,
            SuccessfulAgents = completedContexts.Count(c => c.IsSuccess == true),
            FailedAgents = completedContexts.Count(c => c.IsSuccess == false),
            CancelledAgents = contexts.Count(c => c.IsCancelled),
            TotalRetries = contexts.Sum(c => c.RetryCount),
            AverageExecutionTimeMs = CalculateAverageExecutionTime(contexts),
            ParallelExecutions = contexts.Count(c => c.ExecutionMode == ExecutionMode.Parallel),
            SequentialExecutions = contexts.Count(c => c.ExecutionMode == ExecutionMode.Sequential)
        };
    }

    #endregion

    #region Fork 与权限同步

    public Task<ForkResult> ForkSubAgentAsync(ForkOptions options, CancellationToken ct = default)
    {
        if (_forkManager == null)
            throw new InvalidOperationException("ForkSubAgentManager 未注册");
        return _forkManager.ForkAsync(options, ct);
    }

    public Task SyncAgentPermissionsAsync(string agentId, PermissionSyncRequest request, CancellationToken ct = default)
    {
        if (_permissionBridge == null)
            throw new InvalidOperationException("SwarmPermissionBridge 未注册");
        return _permissionBridge.SyncPermissionsAsync(agentId, request, ct);
    }

    #endregion

    #region 私有方法

    public async Task<JoinCode.Abstractions.Interfaces.ReconnectResult?> ReconnectDisconnectedTeammateAsync(string teamId, string agentId, CancellationToken cancellationToken = default)
    {
        if (_reconnectService is null)
        {
            _logger?.LogWarning("[AgentCoordinator] ITeammateReconnectService 未注册，无法重连");
            return null;
        }

        try
        {
            return await _reconnectService.ReconnectTeammateAsync(teamId, agentId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[AgentCoordinator] 重连 Teammate {AgentId} 失败", agentId);
            return null;
        }
    }

    public async Task<IReadOnlyList<JoinCode.Abstractions.Interfaces.ReconnectResult>> ReconnectAllDisconnectedAsync(string teamId, CancellationToken cancellationToken = default)
    {
        if (_reconnectService is null)
        {
            _logger?.LogWarning("[AgentCoordinator] ITeammateReconnectService 未注册，无法批量重连");
            return [];
        }

        try
        {
            var result = await _reconnectService.ReconnectAllDisconnectedAsync(teamId, cancellationToken).ConfigureAwait(false);
            return new[] { result };
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[AgentCoordinator] 批量重连 Teammate 失败");
            return [];
        }
    }

    private static long? CalculateAverageExecutionTime(List<AgentExecutionContext> contexts)
    {
        var completedContexts = contexts
            .Where(c => c.LastExecutionStart.HasValue && c.LastExecutionEnd.HasValue)
            .ToList();

        if (completedContexts.Count == 0)
        {
            return null;
        }

        var totalMs = completedContexts
            .Sum(c => (c.LastExecutionEnd.GetValueOrDefault() - c.LastExecutionStart.GetValueOrDefault()).TotalMilliseconds);

        return (long)(totalMs / completedContexts.Count);
    }

    #endregion
}
