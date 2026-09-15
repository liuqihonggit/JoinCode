namespace Core.Goal;


/// <summary>
/// Goal Graph 执行引擎 — 事件驱动队列 + 条件路由 + 回退重激活
/// </summary>
[Register(typeof(GoalGraphEngine), ServiceLifetime.Singleton)]
public sealed partial class GoalGraphEngine : ServiceEntity, ISubAgentConcurrencyUpdater
{
    private readonly IChatClient _kernel;
    private readonly IGoalEvaluator _evaluator;
    private readonly IGoalHeartbeat _heartbeat;
    private readonly ILogger<GoalGraphEngine>? _logger;
    private readonly IClockService _clock;
    private readonly IServiceProvider _serviceProvider;
    private readonly IAgentService? _agentService = null!;
    private readonly ICaptainDispatchGuard? _dispatchGuard = null!;
    private readonly ITeamManager? _teamManager = null!;
    private readonly IGoalUserInteraction? _userInteraction = null;
    private readonly IGoalNodeInspector? _nodeInspector = null;
    private readonly IGoalConflictMessenger? _conflictMessenger = null;
    private volatile SubAgentConcurrencyOptions _concurrencyOptions;
    private readonly Dictionary<string, Func<NodeContext, Task<NodeResult>>> _functionRegistry = new(StringComparer.Ordinal);
    private readonly GoalStateUpdater _stateUpdater;
    private readonly RetryHandler _retryHandler;
    private readonly NodeExecutorDispatcher _nodeExecutor;
    private readonly NodeCompletionPipeline _completionPipeline;

    /// <summary>
    /// 构造 GoalGraphEngine — 注入聊天客户端、评估器、服务提供器及各类可选依赖
    /// </summary>
    /// <param name="kernel">聊天客户端</param>
    /// <param name="evaluator">目标评估器</param>
    /// <param name="serviceProvider">服务提供器，用于解析可选依赖</param>
    /// <param name="logger">可选日志记录器</param>
    /// <param name="heartbeat">可选心跳服务，缺省创建新实例</param>
    /// <param name="clock">可选时钟服务，缺省使用系统时钟</param>
    /// <param name="userInteraction">可选用户交互服务</param>
    /// <param name="nodeInspector">可选节点检查器</param>
    /// <param name="conflictMessenger">可选冲突消息队列</param>
    /// <param name="concurrencyOptions">可选并发选项，缺省从服务提供器解析或使用默认值</param>
    public GoalGraphEngine(
        IChatClient kernel,
        IGoalEvaluator evaluator,
        IServiceProvider serviceProvider,
        ILogger<GoalGraphEngine>? logger = null,
        IGoalHeartbeat? heartbeat = null,
        IClockService? clock = null,
        IGoalUserInteraction? userInteraction = null,
        IGoalNodeInspector? nodeInspector = null,
        IGoalConflictMessenger? conflictMessenger = null,
        SubAgentConcurrencyOptions? concurrencyOptions = null)
    {
        _kernel = kernel;
        _evaluator = evaluator;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _heartbeat = heartbeat ?? new GoalHeartbeat();
        _clock = clock ?? SystemClockService.Instance;
        _userInteraction = userInteraction ?? serviceProvider.GetService<IGoalUserInteraction>();
        _nodeInspector = nodeInspector ?? serviceProvider.GetService<IGoalNodeInspector>();
        _conflictMessenger = conflictMessenger ?? serviceProvider.GetService<IGoalConflictMessenger>();
        _agentService = serviceProvider.GetService<IAgentService>();
        _dispatchGuard = serviceProvider.GetService<ICaptainDispatchGuard>();
        _teamManager = serviceProvider.GetService<ITeamManager>();
        _concurrencyOptions = concurrencyOptions ?? serviceProvider.GetService<SubAgentConcurrencyOptions>() ?? new SubAgentConcurrencyOptions();
        _stateUpdater = new GoalStateUpdater(_clock);
        _retryHandler = new RetryHandler(_nodeInspector, _logger);
        _nodeExecutor = new NodeExecutorDispatcher([
            new AgentNodeExecutor(_agentService, _dispatchGuard, _teamManager, _stateUpdater, _logger),
            new FunctionNodeExecutor(_functionRegistry, _serviceProvider, _logger),
            new JoinNodeExecutor()
        ]);
        _completionPipeline = new NodeCompletionPipeline([
            new MetadataExtractHandler(),
            new UserInteractionHandler(_userInteraction, _logger),
            new LoopObservationHandler(_nodeInspector, _logger),
            new TerminationCheckHandler()
        ], _logger);
    }

    /// <summary>
    /// 注册自定义节点函数 — 将节点 ID 映射到执行委托
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    /// <param name="fn">节点执行委托</param>
    public void RegisterFunction(string nodeId, Func<NodeContext, Task<NodeResult>> fn)
    {
        _functionRegistry[nodeId] = fn;
    }

    /// <summary>
    /// 热重载 execute 并发上限 — 原子替换配置引用（ADR 0048）
    /// </summary>
    public void UpdateConcurrencyOptions(SubAgentConcurrencyOptions options)
    {
        Interlocked.Exchange(ref _concurrencyOptions, options);
        _logger?.LogInformation("execute 并发上限已热重载为 {Limit}", options.MaxConcurrentExecutions);
    }

    /// <summary>
    /// 异步执行 Goal Graph — 事件驱动队列推进节点执行，直到抵达终止节点或取消
    /// </summary>
    /// <param name="graph">目标图定义</param>
    /// <param name="goalState">目标状态</param>
    /// <param name="chatHistory">聊天历史</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>最终目标状态</returns>
    public async Task<GoalState> ExecuteAsync(
        GoalGraph graph,
        GoalState goalState,
        MessageList chatHistory,
        CancellationToken ct)
    {
        var context = new GraphExecutionContext
        {
            Graph = graph,
            State = goalState,
            ChatHistory = chatHistory,
            StateLock = new AsyncLock(nameof(GoalGraphEngine)),
            Clock = _clock,
        };

        // T8.3: 接入 team 组件 — 图执行开始时建团队，节点派发的 sub-agent 加入此团队
        if (_teamManager is not null)
        {
            try
            {
                var teamResult = await _teamManager.CreateTeamAsync(
                    teamName: $"goal-{goalState.GoalId}",
                    description: goalState.Objective,
                    initialMembers: null,
                    ct).ConfigureAwait(false);
                if (teamResult.Success && teamResult.Data is not null)
                {
                    context.TeamId = teamResult.Data.TeamId;
                    _logger?.LogInformation("[GoalGraph] 团队已创建: {TeamId} ({TeamName})", context.TeamId, teamResult.Data.TeamName);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("[GoalGraph] 建团队失败，退化为单 Agent 模式: {Message}", ex.Message);
            }
        }

        context.ReadyQueue.Enqueue(graph.StartNodeId);

        var concurrencyOptions = _concurrencyOptions;
        using var concurrencyLimiter = concurrencyOptions.MaxConcurrentExecutions > 0
            ? new AsyncLock(nameof(GoalGraphEngine) + ".Concurrency", concurrencyOptions.MaxConcurrentExecutions, concurrencyOptions.MaxConcurrentExecutions)
            : null;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var batch = DrainReadyBatch(graph, context);

            if (batch.Count == 0)
            {
                if (context.ReadyQueue.IsEmpty)
                    break;
                try
                {
                    await context.NodeCompletedSignal.WaitAsync(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                }
                catch (TimeoutException) { _logger?.LogDebug("[GoalGraph] 节点完成信号等待超时,继续检查"); }
                continue;
            }

            var outcomes = await Task.WhenAll(batch.Select(nodeId =>
                ExecuteWithSemaphoreAsync(nodeId, graph, context, concurrencyLimiter, ct))).ConfigureAwait(false);

            foreach (var outcome in outcomes)
            {
                if (outcome == NodeCompletionOutcome.GoalAchieved)
                {
                    await _stateUpdater.SetGoalStatusAsync(goalState, GoalStatus.Achieved, context, ct).ConfigureAwait(false);
                    return goalState;
                }
                if (outcome == NodeCompletionOutcome.GoalUnmet)
                {
                    await _stateUpdater.SetGoalStatusAsync(goalState, GoalStatus.Unmet, context, ct).ConfigureAwait(false);
                    return goalState;
                }
            }
        }

        return goalState;
    }

    /// <summary>
    /// 从就绪队列批量取出所有上游已完成的节点（同层节点，可并行执行）。
    /// 未就绪节点重新入队，待下一轮处理。
    /// </summary>
    private List<string> DrainReadyBatch(GoalGraph graph, GraphExecutionContext context)
    {
        var batch = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deferred = new List<string>();

        while (context.ReadyQueue.TryDequeue(out var nodeId))
        {
            if (!seen.Add(nodeId))
                continue;

            if (context.CompletedNodes.ContainsKey(nodeId))
                continue;

            if (!context.AreAllUpstreamsCompleted(nodeId))
            {
                deferred.Add(nodeId);
                continue;
            }

            if (!graph.Dag.Nodes.TryGetValue(nodeId, out var dagNode))
            {
                _logger?.LogWarning("[GoalGraph] 节点不存在: {NodeId}", nodeId);
                continue;
            }

            if (dagNode.Payload.Status == GoalNodeStatus.Completed)
                continue;

            batch.Add(nodeId);
        }

        foreach (var id in deferred)
            context.ReadyQueue.Enqueue(id);

        return batch;
    }

    /// <summary>
    /// 在并发限流器控制下执行单个节点完成处理。
    /// </summary>
    private async Task<NodeCompletionOutcome> ExecuteWithSemaphoreAsync(
        string nodeId,
        GoalGraph graph,
        GraphExecutionContext context,
        AsyncLock? limiter,
        CancellationToken ct)
    {
        IDisposable? releaser = null;
        if (limiter is not null)
            releaser = await limiter.TryLockAsync(ct).ConfigureAwait(false)
                ?? throw new System.TimeoutException($"锁 '{limiter.Name}' 等待超时");
        using (releaser)
        {
            var dagNode = graph.Dag.Nodes[nodeId];
            var outcome = await ProcessNodeCompletionAsync(nodeId, dagNode, graph, context, ct).ConfigureAwait(false);
            context.NodeCompletedSignal.Release();
            return outcome;
        }
    }

    /// <summary>
    /// 处理单个节点执行 + 完成后逻辑（失败回退 / 终止判断 / 后继入队 / EndNode 判断）。
    /// 返回节点完成后的整体目标状态决策。
    /// </summary>
    private async Task<NodeCompletionOutcome> ProcessNodeCompletionAsync(
        string nodeId,
        DagNode<GoalNodePayload> dagNode,
        GoalGraph graph,
        GraphExecutionContext context,
        CancellationToken ct)
    {
        var payload = dagNode.Payload;

        await ExecuteNodeAsync(nodeId, dagNode, context, ct).ConfigureAwait(false);
        await _stateUpdater.UpdateGoalStateAsync(context).ConfigureAwait(false);

        if (payload.Status == GoalNodeStatus.Failed)
        {
            context.FailedNodes.TryAdd(nodeId, default);

            var failureOutcome = _retryHandler.CheckFailureRateTermination(context);
            if (failureOutcome is not null)
                return failureOutcome.Value;

            foreach (var edgeId in dagNode.OutEdgeIds)
            {
                if (!graph.Dag.Edges.TryGetValue(edgeId, out var edge))
                    continue;
                if (edge.Label.Length > 0)
                    continue;
                if (!context.CompletedNodes.ContainsKey(edge.ToId) && !context.FailedNodes.ContainsKey(edge.ToId))
                {
                    context.ReadyQueue.Enqueue(edge.ToId);
                }
            }

            if (graph.IsEndNode(nodeId))
                return NodeCompletionOutcome.GoalUnmet;

            return NodeCompletionOutcome.Continue;
        }

        context.CompletedNodes.TryAdd(nodeId, default);

        var postCtx = new NodePostCompletionContext
        {
            NodeId = nodeId,
            Payload = payload,
            Context = context,
            Ct = ct,
        };
        await _completionPipeline.ExecuteAsync(postCtx).ConfigureAwait(false);

        if (postCtx.ShouldTerminateLoop)
        {
            _logger?.LogInformation("[GoalGraph] 循环终止条件满足: {NodeId} (迭代={Iter}, 负评={NegCount}, 协调者终止={CoordTerm})",
                nodeId, context.GlobalLoopIteration, payload.NegativeReviewCount, context.CoordinatorTerminated);
            return NodeCompletionOutcome.GoalAchieved;
        }

        var nextIds = context.GetNextNodeIds(nodeId, payload.Routes, payload.RouteMatchMode);
        foreach (var nextId in nextIds)
        {
            if (context.CompletedNodes.ContainsKey(nextId))
            {
                await _retryHandler.HandleRetryAsync(nextId, context, ct).ConfigureAwait(false);
            }
            else
            {
                context.ReadyQueue.Enqueue(nextId);
            }
        }

        if (graph.IsEndNode(nodeId) && payload.Status == GoalNodeStatus.Completed)
        {
            var allEndsDone = graph.EndNodeIds.All(end => context.CompletedNodes.ContainsKey(end) || end == nodeId);
            if (allEndsDone)
                return NodeCompletionOutcome.GoalAchieved;
        }

        return NodeCompletionOutcome.Continue;
    }

    private async Task ExecuteNodeAsync(string nodeId, DagNode<GoalNodePayload> dagNode, GraphExecutionContext context, CancellationToken ct)
    {
        var payload = dagNode.Payload;
        payload.Status = GoalNodeStatus.Running;
        payload.StartedAt = _clock.GetUtcNow();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(payload.TimeoutSeconds));

        try
        {
            NodeResult result = await _nodeExecutor.ExecuteAsync(nodeId, payload, context, timeoutCts.Token).ConfigureAwait(false);

            payload.Output = result.Output;
            payload.Routes = result.Routes;
            payload.TokensUsed = result.TokensUsed;
            payload.CompletedAt = _clock.GetUtcNow();
            context.TotalTokensConsumed += result.TokensUsed;

            if (result.IsFailed)
            {
                payload.Status = GoalNodeStatus.Failed;
                payload.ErrorMessage = result.Message;
                _logger?.LogWarning("[GoalGraph] {NodeId}({Name}): {Message}", nodeId, payload.Name, result.Message);
            }
            else
            {
                payload.Status = GoalNodeStatus.Completed;
                if (result.Message is not null)
                {
                    _logger?.LogInformation("[GoalGraph] {NodeId}({Name}): {Message}", nodeId, payload.Name, result.Message);
                }
            }

            if (_conflictMessenger is not null && payload.Status == GoalNodeStatus.Completed)
            {
                var conflicts = await _conflictMessenger.DequeueConflictsAsync(nodeId, ct).ConfigureAwait(false);
                if (conflicts.Count > 0)
                {
                    var conflictSummary = string.Join("; ", conflicts.Select(c => $"[{c.SourceNodeId}] {c.Content}"));
                    payload.Output = string.IsNullOrWhiteSpace(payload.Output)
                        ? $"[冲突通知] {conflictSummary}"
                        : $"{payload.Output}\n[冲突通知] {conflictSummary}";
                    _logger?.LogInformation("[GoalGraph] {NodeId} 拉取 {Count} 条冲突消息", nodeId, conflicts.Count);
                }
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            payload.Status = GoalNodeStatus.Failed;
            payload.ErrorMessage = $"Timeout after {payload.TimeoutSeconds}s";
            payload.CompletedAt = _clock.GetUtcNow();
            _logger?.LogWarning("[GoalGraph] {NodeId}({Name}): 超时", nodeId, payload.Name);
        }
        catch (Exception ex)
        {
            payload.Status = GoalNodeStatus.Failed;
            payload.ErrorMessage = ex.Message;
            payload.CompletedAt = _clock.GetUtcNow();
            _logger?.LogError(ex, "[GoalGraph] {NodeId}({Name}): 执行失败", nodeId, payload.Name);
        }
    }
}
