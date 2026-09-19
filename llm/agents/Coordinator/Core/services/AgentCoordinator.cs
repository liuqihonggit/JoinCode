namespace Core.Agents.Coordinator;

/// <summary>
/// Agent协调器 - 提供高级协调功能，包括重试策略、断路器模式、资源清理等
/// 职责：协调 IAgentLifecycleManager、IAgentWorktreeManager、IMailbox、IAgentExecutionEngine
/// </summary>
[Register(typeof(ISubAgentCoordinator), ServiceLifetime.Singleton)]
[Register(typeof(ITeammateObserver), ServiceLifetime.Singleton)]
public sealed partial class AgentCoordinator : ServiceEntity, ISubAgentCoordinator, ITeammateObserver, ISubAgentConcurrencyUpdater {
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly IAgentWorktreeManager _worktreeManager;
    private readonly IMailbox _messageBroker;
    private readonly IAgentExecutionEngine _executionEngine;
    private readonly IClockService _clock;
    private readonly ILogger<AgentCoordinator>? _logger;
    private readonly SubagentStopHookRunner _stopHookRunner;
    private readonly IForkSubAgentManager? _forkManager;
    private readonly ISwarmPermissionBridge? _permissionBridge;
    private readonly TeammateReconnectDispatcher _reconnectDispatcher;

    private readonly ConcurrentDictionary<string, AgentExecutionContext> _executionContexts;
    private readonly Core.Lifecycle.AgentStartTimer _agentStartTimer = new();
    private readonly SecretaryRegistry _secretaryRegistry;
    private readonly MiddlewarePipeline<AgentDisposeContext> _disposePipeline;
    private readonly MiddlewarePipeline<UnifiedSpawnContext> _spawnPipeline;
    private volatile AsyncLock _spawnSemaphore;

    /// <summary>Agent 任务状态变更事件，参数携带 Agent ID 与新旧状态</summary>
    public event EventHandler<AgentTaskStatusChangedEventArgs>? TaskStatusChanged;
    /// <summary>队友变更事件，参数携带 Agent ID 与新旧状态（按 AgentStatus 映射）</summary>
    public event EventHandler<TeammateChangedEventArgs>? TeammateChanged;

    /// <summary>
    /// 构造 Agent 协调器实例
    /// </summary>
    /// <param name="core">核心依赖包，包含生命周期、Worktree、消息邮箱、执行引擎与状态机</param>
    /// <param name="clock">时钟服务，用于记录执行时间</param>
    /// <param name="disposePipeline">Agent 释放管道</param>
    /// <param name="spawnPipeline">Agent 生成管道</param>
    /// <param name="permission">可选权限依赖包，包含 Swarm 权限桥接</param>
    /// <param name="team">可选团队依赖包，包含队友重连服务</param>
    /// <param name="forkManager">可选 Fork 子代理管理器</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="subAgentContextAccessor">可选子代理上下文访问器，缺省时使用默认实现</param>
    /// <param name="subagentStopHookManager">可选子代理停止 Hook 管理器</param>
    /// <param name="concurrencyOptions">可选并发选项，控制 spawn 并发上限</param>
    /// <param name="autoRebaseService">可选自动 rebase 服务，用于 SubagentStop 时同步主干</param>
    public AgentCoordinator(
        AgentCoreDependencies core,
        IClockService clock,
        MiddlewarePipeline<AgentDisposeContext> disposePipeline,
        MiddlewarePipeline<UnifiedSpawnContext> spawnPipeline,
        AgentPermissionDependencies? permission = null,
        AgentTeamDependencies? team = null,
        IForkSubAgentManager? forkManager = null,
        ILogger<AgentCoordinator>? logger = null,
        ISubAgentContextAccessor? subAgentContextAccessor = null,
        ISubagentStopHookManager? subagentStopHookManager = null,
        SubAgentConcurrencyOptions? concurrencyOptions = null,
        IAutoRebaseService? autoRebaseService = null) {
        _lifecycleManager = core.LifecycleManager ?? throw new ArgumentNullException(nameof(core.LifecycleManager));
        _worktreeManager = core.WorktreeManager ?? throw new ArgumentNullException(nameof(core.WorktreeManager));
        _messageBroker = core.MessageBroker ?? throw new ArgumentNullException(nameof(core.MessageBroker));
        _executionEngine = core.ExecutionEngine ?? throw new ArgumentNullException(nameof(core.ExecutionEngine));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _disposePipeline = disposePipeline ?? throw new ArgumentNullException(nameof(disposePipeline));
        _spawnPipeline = spawnPipeline ?? throw new ArgumentNullException(nameof(spawnPipeline));
        _logger = logger;
        _stopHookRunner = new SubagentStopHookRunner(subAgentContextAccessor, subagentStopHookManager, autoRebaseService, _logger);
        _forkManager = forkManager;
        _permissionBridge = permission?.PermissionBridge;
        _reconnectDispatcher = new TeammateReconnectDispatcher(team?.ReconnectService, _logger);
        _executionContexts = new ConcurrentDictionary<string, AgentExecutionContext>();
        _secretaryRegistry = new SecretaryRegistry((task, opts, ct) => SpawnSubAgentAsync(task, opts, ct), _logger);

        var spawnLimit = Math.Max(1, (concurrencyOptions ?? new SubAgentConcurrencyOptions()).MaxConcurrentSpawns);
        _spawnSemaphore = new AsyncLock(nameof(AgentCoordinator) + ".Spawn", spawnLimit, spawnLimit);

        core.StateMachine.StateChanged += (_, e) => {
            TaskStatusChanged?.Invoke(this, new AgentTaskStatusChangedEventArgs(e.AgentId, e.OldState, e.NewState));
            TeammateChanged?.Invoke(this, new TeammateChangedEventArgs {
                AgentId = e.AgentId,
                OldState = e.OldState.ToAgentStatus(),
                NewState = e.NewState.ToAgentStatus(),
            });
        };
    }

    /// <summary>
    /// 释放 spawn 信号量等内核资源
    /// </summary>
    public override void Dispose() {
        _spawnSemaphore.Dispose();
        base.Dispose();
    }

    /// <summary>
    /// 热重载 spawn 并发上限 — 原子替换 SemaphoreSlim，旧的 Dispose（ADR 0048）
    /// </summary>
    public void UpdateConcurrencyOptions(SubAgentConcurrencyOptions options) {
        var newLimit = Math.Max(1, options.MaxConcurrentSpawns);
        var newSem = new AsyncLock(nameof(AgentCoordinator) + ".Spawn", newLimit, newLimit);
        var old = Interlocked.Exchange(ref _spawnSemaphore, newSem);
        old.Dispose();
        _logger?.LogInformation("spawn 并发上限已热重载为 {Limit}", newLimit);
    }

    #region Agent 生命周期管理（含协调逻辑）

    /// <summary>
    /// 生成单个子 Agent — 通过 spawn 信号量限流后委托 spawn 管道执行
    /// </summary>
    /// <param name="task">子 Agent 任务描述</param>
    /// <param name="options">子 Agent 选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="parentSessionId">可选父会话 ID，用于生成自定义唯一标识</param>
    /// <returns>已生成的子 Agent 实例</returns>
    public async Task<IAgent> SpawnSubAgentAsync(string task, SubAgentOptions? options = null, CancellationToken cancellationToken = default, string? parentSessionId = null) {
        var sem = _spawnSemaphore;
        IDisposable? releaser;
        try {
            releaser = await sem.TryLockAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new System.TimeoutException($"锁 '{sem.Name}' 等待超时");
        } catch (ObjectDisposedException) {
            sem = _spawnSemaphore;
            releaser = await sem.TryLockAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new System.TimeoutException($"锁 '{sem.Name}' 等待超时");
        }
        try {
            var ctx = new UnifiedSpawnContext {
                Task = task,
                SubOptions = options,
                CancellationToken = cancellationToken,
                ParentSessionId = parentSessionId,
            };
            await _spawnPipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);

            if (ctx.Agent is not null) {
                if (ctx.SpawnedAt != default) {
                    _agentStartTimer.Record(ctx.AgentId, ctx.SpawnedAt);
                }
                if (ctx.ExecutionContext is not null) {
                    _executionContexts[ctx.AgentId] = ctx.ExecutionContext;
                }
            }

            return ctx.Agent ?? throw new InvalidOperationException("Spawn pipeline completed without agent");
        } finally {
            releaser.DisposeSafe(_logger);
        }
    }

    /// <summary>
    /// 批量生成子 Agent — 并行调用 SpawnSubAgentAsync
    /// </summary>
    /// <param name="tasks">子 Agent 任务描述集合</param>
    /// <param name="options">共享的子 Agent 选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>已生成的子 Agent 列表</returns>
    public async Task<IReadOnlyList<IAgent>> SpawnSubAgentsAsync(IEnumerable<string> tasks, SubAgentOptions? options = null, CancellationToken cancellationToken = default) {
        var taskList = tasks.ToList();
        var spawnTasks = taskList
            .Select(task => SpawnSubAgentAsync(task, options, cancellationToken))
            .ToList();

        var agents = await Task.WhenAll(spawnTasks).ConfigureAwait(false);
        return agents.ToList();
    }

    /// <summary>
    /// T2.4: 确保队长秘书已 spawn 常驻 — 委托给 SecretaryRegistry
    /// 秘书职责：队长改热文件时找调用点+批量改+编译自检；整理任务表(DAG)；发广播邮件；记录任务状态
    /// 通信：队长通过 IMailbox 给秘书派活，秘书做完回结果
    /// </summary>
    /// <param name="ownerId">队长标识（goalId 或 agentId）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>秘书的 agentId</returns>
    public Task<string> EnsureSecretaryAsync(string ownerId, CancellationToken cancellationToken = default) {
        return _secretaryRegistry.EnsureSecretaryAsync(ownerId, cancellationToken);
    }

    /// <summary>
    /// T2.4: 获取队长的秘书 agentId（已 spawn 则返回，未 spawn 则 null）— 委托给 SecretaryRegistry
    /// </summary>
    public string? GetSecretaryId(string ownerId) => _secretaryRegistry.GetSecretaryId(ownerId);

    /// <summary>
    /// 执行单个 Agent — 记录执行起止时间与结果到执行上下文
    /// </summary>
    /// <param name="agent">要执行的 Agent</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果</returns>
    public async Task<SubAgentResult> ExecuteAsync(IAgent agent, CancellationToken cancellationToken = default) {
        if (!_executionContexts.TryGetValue(agent.ObjectId.UniqueId, out var context)) {
            context = new AgentExecutionContext {
                AgentId = agent.ObjectId.UniqueId,
                Task = agent.Task,
                SpawnedAt = _clock.GetUtcNow(),
                RetryCount = 0
            };
            _executionContexts[agent.ObjectId.UniqueId] = context;
        }

        context.LastExecutionStart = _clock.GetUtcNow();

        try {
            var result = await _lifecycleManager.ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);

            context.LastExecutionEnd = _clock.GetUtcNow();
            context.Outcome = result.IsSuccess ? AgentOutcome.Succeeded : AgentOutcome.Failed;

            if (result.IsSuccess) {
                _logger?.LogInformation("[AgentCoordinator] Agent {AgentId} 执行成功", agent.ObjectId.UniqueId);
            } else {
                _logger?.LogWarning("[AgentCoordinator] Agent {AgentId} 执行失败: {Error}", agent.ObjectId.UniqueId, result.Error);
            }

            return result;
        } catch (Exception ex) {
            context.LastExecutionEnd = _clock.GetUtcNow();
            context.Outcome = AgentOutcome.Failed;
            _logger?.LogError(ex, "[AgentCoordinator] Agent {AgentId} 执行异常", agent.ObjectId.UniqueId);
            throw;
        }
    }

    /// <summary>
    /// 暂停指定 Agent，委托给生命周期管理器
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功暂停</returns>
    public Task<bool> PauseAgentAsync(string agentId, CancellationToken ct = default) {
        _logger?.LogInformation("[AgentCoordinator] 暂停Agent {AgentId}", agentId);
        return _lifecycleManager.PauseAgentAsync(agentId, ct);
    }

    /// <summary>
    /// 恢复指定 Agent，委托给生命周期管理器
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功恢复</returns>
    public Task<bool> ResumeAgentAsync(string agentId, CancellationToken ct = default) {
        _logger?.LogInformation("[AgentCoordinator] 恢复Agent {AgentId}", agentId);
        return _lifecycleManager.ResumeAgentAsync(agentId, ct);
    }

    /// <summary>
    /// 取消指定 Agent，同时标记执行上下文为已取消
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>是否成功取消</returns>
    public async Task<bool> CancelAgentAsync(string agentId, CancellationToken ct = default) {
        _logger?.LogInformation("[AgentCoordinator] 取消Agent {AgentId}", agentId);

        if (_executionContexts.TryGetValue(agentId, out var context)) {
            context.Outcome = AgentOutcome.Cancelled;
        }

        return await _lifecycleManager.CancelAgentAsync(agentId, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 取消所有 Agent，同时将所有执行上下文标记为已取消
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task CancelAllAsync(CancellationToken ct = default) {
        _logger?.LogInformation("[AgentCoordinator] 取消所有Agent");

        foreach (var context in _executionContexts.Values) {
            context.Outcome = AgentOutcome.Cancelled;
        }

        await _lifecycleManager.CancelAllAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 使用默认重试策略重试指定 Agent
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重试结果；找不到上下文或达到最大重试次数时返回 null</returns>
    public async Task<SubAgentResult?> RetryAsync(string agentId, CancellationToken cancellationToken = default) {
        return await RetryWithPolicyAsync(agentId, RetryPolicy.Default, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 使用指定策略重试Agent
    /// </summary>
    public async Task<SubAgentResult?> RetryWithPolicyAsync(string agentId, RetryPolicy policy, CancellationToken cancellationToken = default) {
        if (!_executionContexts.TryGetValue(agentId, out var context)) {
            _logger?.LogWarning("[AgentCoordinator] 无法找到Agent {AgentId} 的执行上下文", agentId);
            return null;
        }

        if (context.RetryCount >= policy.MaxRetries) {
            _logger?.LogWarning("[AgentCoordinator] Agent {AgentId} 已达到最大重试次数 {MaxRetries}", agentId, policy.MaxRetries);
            return null;
        }

        context.RetryCount++;
        var delay = policy.GetDelay(context.RetryCount);

        _logger?.LogInformation("[AgentCoordinator] 等待 {DelayMs}ms 后重试Agent {AgentId} (第{RetryCount}次)",
            delay.TotalMilliseconds, agentId, context.RetryCount);

        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);

        var result = await _lifecycleManager.RetryAsync(agentId, cancellationToken).ConfigureAwait(false);

        if (result != null) {
            context.Outcome = result.IsSuccess ? AgentOutcome.Succeeded : AgentOutcome.Failed;
            if (result.IsSuccess) {
                _logger?.LogInformation("[AgentCoordinator] Agent {AgentId} 重试成功", agentId);
            } else if (context.RetryCount < policy.MaxRetries) {
                _logger?.LogWarning("[AgentCoordinator] Agent {AgentId} 重试失败，还可重试 {Remaining} 次",
                    agentId, policy.MaxRetries - context.RetryCount);
            }
        }

        return result;
    }

    /// <summary>
    /// 执行Agent并在失败时自动重试
    /// </summary>
    public async Task<SubAgentResult> ExecuteWithRetryAsync(AgentBase agent, RetryPolicy? policy = null, CancellationToken cancellationToken = default) {
        policy ??= RetryPolicy.Default;
        var result = await ExecuteAsync(agent, cancellationToken).ConfigureAwait(false);

        while (!result.IsSuccess && !cancellationToken.IsCancellationRequested) {
            if (!_executionContexts.TryGetValue(agent.ObjectId.UniqueId, out var context)) {
                break;
            }

            if (context.RetryCount >= policy.MaxRetries) {
                break;
            }

            var retryResult = await RetryWithPolicyAsync(agent.ObjectId.UniqueId, policy, cancellationToken).ConfigureAwait(false);
            if (retryResult == null) {
                break;
            }

            result = retryResult;
        }

        return result;
    }

    /// <summary>
    /// 释放指定 Agent 资源 — 触发 SubagentStop Hook 后执行释放管道并清理执行上下文
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task DisposeAgentAsync(string agentId, CancellationToken cancellationToken = default) {
        _logger?.LogInformation("[AgentCoordinator] 释放Agent {AgentId} 资源", agentId);

        await _stopHookRunner.OnSubagentStopHookAsync(agentId, cancellationToken).ConfigureAwait(false);

        var ctx = new AgentDisposeContext {
            AgentId = agentId,
            CancellationToken = cancellationToken,
        };
        await _disposePipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);

        _executionContexts.TryRemove(agentId, out _);
        _agentStartTimer.Remove(agentId);
    }

    #endregion

    #region 执行策略（含协调逻辑）

    /// <summary>
    /// 并行执行多个 Agent，委托给执行引擎
    /// </summary>
    /// <param name="agents">要执行的 Agent 集合</param>
    /// <param name="options">并行执行选项</param>
    /// <param name="clusterOptions">集群执行选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>各 Agent 的执行结果列表</returns>
    public async Task<IReadOnlyList<SubAgentResult>> ExecuteParallelAsync(
        IEnumerable<IAgent> agents,
        ParallelOptions? options = null,
        ClusterExecutionOptions? clusterOptions = null,
        CancellationToken cancellationToken = default) {
        var agentList = agents.ToList();
        _logger?.LogInformation("[AgentCoordinator] 并行执行 {Count} 个Agent", agentList.Count);

        foreach (var agent in agentList) {
            if (_executionContexts.TryGetValue(agent.ObjectId.UniqueId, out var context)) {
                context.ExecutionMode = ExecutionMode.Parallel;
            }
        }

        return await _executionEngine.ExecuteParallelAsync(agentList, options, clusterOptions, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 串行执行多个 Agent，委托给执行引擎
    /// </summary>
    /// <param name="agents">要执行的 Agent 集合</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>各 Agent 的执行结果列表</returns>
    public async Task<IReadOnlyList<SubAgentResult>> ExecuteSequentialAsync(
        IEnumerable<IAgent> agents,
        CancellationToken cancellationToken = default) {
        var agentList = agents.ToList();
        _logger?.LogInformation("[AgentCoordinator] 串行执行 {Count} 个Agent", agentList.Count);

        foreach (var agent in agentList) {
            if (_executionContexts.TryGetValue(agent.ObjectId.UniqueId, out var context)) {
                context.ExecutionMode = ExecutionMode.Sequential;
            }
        }

        return await _executionEngine.ExecuteSequentialAsync(agentList, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 执行主Agent，失败时依次尝试备用Agent
    /// </summary>
    public async Task<FallbackExecutionResult> ExecuteWithFallbackAsync(
        IAgent primaryAgent,
        IEnumerable<IAgent> fallbackAgents,
        CancellationToken cancellationToken = default) {
        var fallbacks = fallbackAgents.ToList();
        _logger?.LogInformation("[AgentCoordinator] 执行主Agent {AgentId}，准备 {FallbackCount} 个备用Agent",
            primaryAgent.ObjectId.UniqueId, fallbacks.Count);

        var results = new List<SubAgentResult>();
        var primaryResult = await ExecuteAsync(primaryAgent, cancellationToken).ConfigureAwait(false);
        results.Add(primaryResult);

        if (primaryResult.IsSuccess) {
            return new FallbackExecutionResult {
                AllResults = results,
                SuccessfulResult = primaryResult,
                SuccessAgentId = primaryAgent.ObjectId.UniqueId,
                AttemptCount = 1
            };
        }

        _logger?.LogWarning("[AgentCoordinator] 主Agent {AgentId} 失败，尝试备用Agent", primaryAgent.ObjectId.UniqueId);

        foreach (var fallback in fallbacks) {
            if (cancellationToken.IsCancellationRequested) {
                break;
            }

            var fallbackResult = await ExecuteAsync(fallback, cancellationToken).ConfigureAwait(false);
            results.Add(fallbackResult);

            if (fallbackResult.IsSuccess) {
                _logger?.LogInformation("[AgentCoordinator] 备用Agent {AgentId} 执行成功", fallback.ObjectId.UniqueId);
                return new FallbackExecutionResult {
                    AllResults = results,
                    SuccessfulResult = fallbackResult,
                    SuccessAgentId = fallback.ObjectId.UniqueId,
                    AttemptCount = results.Count
                };
            }
        }

        _logger?.LogError("[AgentCoordinator] 所有备用Agent均失败");
        return new FallbackExecutionResult {
            AllResults = results,
            SuccessfulResult = null,
            SuccessAgentId = null,
            AttemptCount = results.Count
        };
    }

    #endregion

    #region 消息通信（含协调逻辑）

    /// <summary>
    /// 向指定 Agent 发送消息；Agent 不存在或已终止时返回 false
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="message">要发送的消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功投递</returns>
    public async Task<bool> SendMessageAsync(string agentId, CoordinatorAgentMessage message, CancellationToken cancellationToken = default) {
        var agent = await _lifecycleManager.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agent == null) {
            _logger?.LogWarning("[AgentCoordinator] 无法向不存在的Agent {AgentId} 发送消息", agentId);
            return false;
        }

        if (((AgentBase)agent).State.IsTerminal()) {
            _logger?.LogWarning("[AgentCoordinator] Agent {AgentId} 已结束，无法接收消息", agentId);
            return false;
        }

        return await _messageBroker.SendAsync(agentId, message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 向所有活跃 Agent 广播消息
    /// </summary>
    /// <param name="message">要广播的消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task BroadcastAsync(CoordinatorAgentMessage message, CancellationToken cancellationToken = default) {
        var allAgents = await _lifecycleManager.GetAllAgentsAsync(cancellationToken).ConfigureAwait(false);
        var activeAgentCount = allAgents?.Count(a => !((AgentBase)a).State.IsTerminal()) ?? 0;

        _logger?.LogInformation("[AgentCoordinator] 广播消息给 {Count} 个活跃Agent", activeAgentCount);

        await _messageBroker.BroadcastAsync(message, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取指定 Agent 的消息流
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>消息异步枚举流</returns>
    public IAsyncEnumerable<CoordinatorAgentMessage> ReadMessagesAsync(string agentId, CancellationToken cancellationToken = default) {
        return _messageBroker.ReceiveAsync(agentId, cancellationToken);
    }

    #endregion

    #region 查询和报告（含协调逻辑）

    /// <summary>
    /// 获取指定 Agent 的执行结果
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>执行结果；不存在时返回 null</returns>
    public async Task<SubAgentResult?> GetResultAsync(string agentId, CancellationToken cancellationToken = default) {
        return await _lifecycleManager.GetResultAsync(agentId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取所有 Agent 的执行结果字典
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Agent ID 到执行结果的映射</returns>
    public async Task<IReadOnlyDictionary<string, SubAgentResult>> GetAllResultsAsync(CancellationToken cancellationToken = default) {
        return await _lifecycleManager.GetAllResultsAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 等待所有 Agent 进入终态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public Task WaitAllAsync(CancellationToken cancellationToken = default) => _lifecycleManager.WaitAllAsync(cancellationToken);

    /// <summary>
    /// 获取 Agent 状态报告
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>状态报告</returns>
    public async Task<AgentStateReport> GetStateReportAsync(CancellationToken cancellationToken = default) {
        return await _lifecycleManager.GetStateReportAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取协调器综合报告 — 包含状态统计、平均执行时间与重试统计
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>协调器报告</returns>
    public async Task<CoordinatorReport> GetCoordinatorReportAsync(CancellationToken cancellationToken = default) {
        var stateReport = await _lifecycleManager.GetStateReportAsync(cancellationToken).ConfigureAwait(false);
        var contexts = _executionContexts.Values;

        return new CoordinatorReport {
            TotalAgents = stateReport.TotalAgents,
            PendingCount = stateReport.PendingCount,
            RunningCount = stateReport.RunningCount,
            PausedCount = stateReport.PausedCount,
            CompletedCount = stateReport.CompletedCount,
            FailedCount = stateReport.FailedCount,
            CancelledCount = stateReport.CancelledCount,
            Agents = stateReport.Agents.Select(a => new AgentInfo {
                Id = a.AgentId,
                Task = a.Task,
                State = a.CurrentState,
                ExecutionTimeMs = a.ExecutionTimeMs
            }).ToList(),
            AverageExecutionTimeMs = ExecutionStatisticsCalculator.CalculateAverageExecutionTime(contexts),
            TotalRetries = contexts.Sum(c => c.RetryCount),
            AgentsWithRetries = contexts.Count(c => c.RetryCount > 0)
        };
    }

    /// <summary>
    /// 获取指定 Agent 的 Worktree 会话
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Worktree 会话；不存在时返回 null</returns>
    public Task<AgentWorktreeSession?> GetWorktreeSessionAsync(string agentId, CancellationToken cancellationToken = default) {
        return _worktreeManager.GetWorktreeSessionAsync(agentId, cancellationToken);
    }

    /// <summary>
    /// 获取所有 Worktree 会话
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>Agent ID 到 Worktree 会话的映射</returns>
    public Task<IReadOnlyDictionary<string, AgentWorktreeSession>> GetAllWorktreeSessionsAsync(CancellationToken cancellationToken = default) {
        return _worktreeManager.GetAllWorktreeSessionsAsync(cancellationToken);
    }

    /// <summary>
    /// 获取Agent的执行上下文
    /// </summary>
    public AgentExecutionContext? GetExecutionContext(string agentId) {
        _executionContexts.TryGetValue(agentId, out var context);
        return context;
    }

    /// <summary>
    /// 获取Agent的执行持续时间
    /// </summary>
    public TimeSpan? GetAgentExecutionDuration(string agentId) {
        if (_agentStartTimer.TryGet(agentId) is not { } startTime) {
            return null;
        }

        if (_executionContexts.TryGetValue(agentId, out var context) && context.LastExecutionEnd.HasValue) {
            return context.LastExecutionEnd.Value - startTime;
        }

        return _clock.GetUtcNow() - startTime;
    }

    #endregion

    #region Agent 协调功能（原 IAgentCoordinator，已合并到 IAgentService）

    /// <summary>
    /// 停止指定 Agent — 取消其令牌并标记为已取消状态
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>是否成功停止</returns>
    public async Task<bool> StopAgentAsync(string agentId, CancellationToken cancellationToken = default) {
        var agent = await _lifecycleManager.GetAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
        if (agent == null) {
            _logger?.LogWarning("[AgentCoordinator] 无法停止不存在的Agent {AgentId}", agentId);
            return false;
        }

        if (((AgentBase)agent).State != TaskExecutionStatus.Running && ((AgentBase)agent).State != TaskExecutionStatus.Pending) {
            _logger?.LogWarning("[AgentCoordinator] Agent {AgentId} 状态为 {State}，无法停止", agentId, ((AgentBase)agent).State);
            return false;
        }

        _logger?.LogInformation("[AgentCoordinator] 停止Agent {AgentId}", agentId);

        ((AgentBase)agent).CancellationTokenSource?.Cancel();
        ((AgentBase)agent).State = TaskExecutionStatus.Cancelled;

        if (_executionContexts.TryGetValue(agentId, out var context)) {
            context.Outcome = AgentOutcome.Cancelled;
        }

        return await _lifecycleManager.CancelAgentAsync(agentId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取正在运行的 Agent 列表
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>运行中 Agent 信息集合</returns>
    public Task<IEnumerable<RunningAgentInfo>> GetRunningAgentsAsync(CancellationToken cancellationToken = default) {
        return _lifecycleManager.GetRunningAgentsAsync(cancellationToken);
    }

    /// <summary>
    /// 获取正在运行或暂停的队友列表
    /// </summary>
    /// <returns>队友信息列表</returns>
    public async Task<IReadOnlyList<TeammateInfo>> GetRunningTeammatesAsync() {
        var report = await _lifecycleManager.GetStateReportAsync(CancellationToken.None).ConfigureAwait(false);
        return report.Agents
            .Where(a => a.CurrentState == TaskExecutionStatus.Running || a.CurrentState == TaskExecutionStatus.Paused)
            .Select(MapToTeammateInfo)
            .ToList();
    }

    private static TeammateInfo MapToTeammateInfo(AgentStateInfo info) {
        var options = info.Options;
        var progress = info.Progress;
        return new TeammateInfo {
            Id = info.AgentId,
            DisplayName = options?.DisplayName ?? info.AgentId,
            SpinnerVerb = options?.SpinnerVerb ?? "Working",
            ColorHex = options?.ColorHex ?? "#2587EB",
            State = info.CurrentState.ToAgentStatus(),
            StartedAt = info.StartedAt,
            TokenCount = progress?.TokenCount ?? 0,
            ToolUseCount = progress?.ToolUseCount ?? 0,
            LastActivity = progress?.LastActivity?.ActivityDescription ?? info.Task,
            RecentActivities = progress?.RecentActivities?.ToList(),
        };
    }

    #endregion

    #region 批量操作

    /// <summary>
    /// 批量释放所有Agent资源（并行执行）
    /// </summary>
    public async Task DisposeAllAgentsAsync(CancellationToken cancellationToken = default) {
        var agentIds = _executionContexts.Keys.ToList();

        _logger?.LogInformation("[AgentCoordinator] 批量释放 {Count} 个Agent资源", agentIds.Count);

        var disposeTasks = agentIds.Select(agentId => DisposeAgentAsync(agentId, cancellationToken));
        await Task.WhenAll(disposeTasks).ConfigureAwait(false);
    }

    /// <summary>
    /// 获取执行统计信息 — 委托给 ExecutionStatisticsCalculator
    /// </summary>
    public ExecutionStatistics GetExecutionStatistics() {
        return ExecutionStatisticsCalculator.BuildStatistics(_executionContexts);
    }

    #endregion

    #region Fork 与权限同步

    /// <summary>
    /// Fork 子代理 — 委托给 Fork 子代理管理器
    /// </summary>
    /// <param name="options">Fork 选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>Fork 结果</returns>
    public Task<ForkResult> ForkSubAgentAsync(ForkOptions options, CancellationToken ct = default) {
        if (_forkManager == null)
            throw new InvalidOperationException("[AGT006] ForkSubAgentManager 未注册");
        return _forkManager.ForkAsync(options, ct);
    }

    /// <summary>
    /// 同步 Agent 权限 — 委托给 Swarm 权限桥接
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="request">权限同步请求</param>
    /// <param name="ct">取消令牌</param>
    public Task SyncAgentPermissionsAsync(string agentId, PermissionSyncRequest request, CancellationToken ct = default) {
        if (_permissionBridge == null)
            throw new InvalidOperationException("[AGT007] SwarmPermissionBridge 未注册");
        return _permissionBridge.SyncPermissionsAsync(agentId, request, ct);
    }

    #endregion

    #region 私有方法

    /// <summary>
    /// 重连已断开的队友 — 委托给 TeammateReconnectDispatcher
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="agentId">目标队友标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重连结果；服务未注册或重连失败时返回 null</returns>
    public Task<JoinCode.Abstractions.Interfaces.ReconnectResult?> ReconnectDisconnectedTeammateAsync(string teamId, string agentId, CancellationToken cancellationToken = default) {
        return _reconnectDispatcher.ReconnectDisconnectedTeammateAsync(teamId, agentId, cancellationToken);
    }

    /// <summary>
    /// 批量重连团队中所有已断开的队友 — 委托给 TeammateReconnectDispatcher
    /// </summary>
    /// <param name="teamId">团队标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>重连结果列表；服务未注册时返回空列表</returns>
    public Task<IReadOnlyList<JoinCode.Abstractions.Interfaces.ReconnectResult>> ReconnectAllDisconnectedAsync(string teamId, CancellationToken cancellationToken = default) {
        return _reconnectDispatcher.ReconnectAllDisconnectedAsync(teamId, cancellationToken);
    }

    #endregion
}