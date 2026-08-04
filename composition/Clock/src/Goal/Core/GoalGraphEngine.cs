namespace Core.Goal;

using JoinCode.Abstractions.Models.Goal;
using JoinCode.Abstractions.LLM;
using JoinCode.Abstractions.Interfaces;
using Structura.Dag;

/// <summary>
/// Goal Graph 执行引擎 — 事件驱动队列 + 条件路由 + 回退重激活
/// </summary>
[Register]
public sealed partial class GoalGraphEngine
{
    private readonly IChatClient _kernel;
    private readonly IGoalEvaluator _evaluator;
    private readonly IGoalHeartbeat _heartbeat;
    [Inject] private readonly ILogger<GoalGraphEngine>? _logger;
    [Inject] private readonly IClockService _clock;
    [Inject] private readonly IServiceProvider _serviceProvider;
    private Core.Agents.Coordinator.AgentRegistry _agentRegistry => Core.Agents.Coordinator.Agent.Registry;
    [Inject] private readonly IAgentService? _agentService = null!;
    [Inject] private readonly IGoalUserInteraction? _userInteraction = null;
    [Inject] private readonly IGoalLoopObserver? _loopObserver = null;
    private readonly Dictionary<string, Func<NodeContext, Task<NodeResult>>> _functionRegistry = new(StringComparer.Ordinal);

    public GoalGraphEngine(
        IChatClient kernel,
        IGoalEvaluator evaluator,
        IServiceProvider serviceProvider,
        ILogger<GoalGraphEngine>? logger = null,
        IGoalHeartbeat? heartbeat = null,
        IClockService? clock = null,
        IGoalUserInteraction? userInteraction = null,
        IGoalLoopObserver? loopObserver = null)
    {
        _kernel = kernel;
        _evaluator = evaluator;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _heartbeat = heartbeat ?? new GoalHeartbeat();
        _clock = clock ?? SystemClockService.Instance;
        _userInteraction = userInteraction ?? serviceProvider.GetService<IGoalUserInteraction>();
        _loopObserver = loopObserver ?? serviceProvider.GetService<IGoalLoopObserver>();
    }

    public void RegisterFunction(string nodeId, Func<NodeContext, Task<NodeResult>> fn)
    {
        _functionRegistry[nodeId] = fn;
    }

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
            StateLock = new SemaphoreSlim(1, 1),
            Clock = _clock,
        };

        context.ReadyQueue.Enqueue(graph.StartNodeId);

        while (context.ReadyQueue.TryDequeue(out var nodeId))
        {
            ct.ThrowIfCancellationRequested();

            if (context.CompletedNodes.Contains(nodeId))
                continue;

            if (!context.AreAllUpstreamsCompleted(nodeId))
            {
                context.ReadyQueue.Enqueue(nodeId);
                await Task.Delay(50, ct).ConfigureAwait(false);
                continue;
            }

            if (!graph.Dag.Nodes.TryGetValue(nodeId, out var dagNode))
            {
                _logger?.LogWarning("[GoalGraph] 节点不存在: {NodeId}", nodeId);
                continue;
            }

            var payload = dagNode.Payload;

            if (payload.Status == GoalNodeStatus.Completed)
                continue;

            await ExecuteNodeAsync(nodeId, dagNode, context, ct).ConfigureAwait(false);

            await UpdateGoalStateAsync(context).ConfigureAwait(false);

            if (payload.Status == GoalNodeStatus.Failed)
            {
                context.FailedNodes.Add(nodeId);

                foreach (var edgeId in dagNode.OutEdgeIds)
                {
                    if (!graph.Dag.Edges.TryGetValue(edgeId, out var edge))
                        continue;
                    if (edge.Label.Length > 0)
                        continue;
                    if (!context.CompletedNodes.Contains(edge.ToId) && !context.FailedNodes.Contains(edge.ToId))
                    {
                        context.ReadyQueue.Enqueue(edge.ToId);
                    }
                }

                if (graph.IsEndNode(nodeId))
                {
                    await context.StateLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        goalState.Status = GoalStatus.Unmet;
                        goalState.AchievedAt = _clock.GetUtcNow();
                    }
                    finally { context.StateLock.Release(); }
                    return goalState;
                }
                continue;
            }

            context.CompletedNodes.Add(nodeId);

            ExtractNegReviewMetadata(nodeId, payload, context);

            await HandleUserInteractionAsync(nodeId, payload, context, ct).ConfigureAwait(false);

            await HandleLoopObservationAsync(nodeId, payload, context, ct).ConfigureAwait(false);

            if (ShouldTerminateLoop(nodeId, payload, context, graph))
            {
                _logger?.LogInformation("[GoalGraph] 循环终止条件满足: {NodeId} (迭代={Iter}, 负评={NegCount}, 协调者终止={CoordTerm})",
                    nodeId, context.GlobalLoopIteration, payload.NegativeReviewCount, context.CoordinatorTerminated);

                var endIds = graph.EndNodeIds;
                await context.StateLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    goalState.Status = GoalStatus.Achieved;
                    goalState.AchievedAt = _clock.GetUtcNow();
                }
                finally { context.StateLock.Release(); }
                return goalState;
            }


            var nextIds = context.GetNextNodeIds(nodeId, payload.Routes, payload.RouteMatchMode);

            foreach (var nextId in nextIds)
            {
                if (context.CompletedNodes.Contains(nextId))
                {
                    await HandleRetryAsync(nextId, context, ct).ConfigureAwait(false);
                }
                else
                {
                    context.ReadyQueue.Enqueue(nextId);
                }
            }

            if (graph.IsEndNode(nodeId) && payload.Status == GoalNodeStatus.Completed)
            {
                var allEndsDone = graph.EndNodeIds.All(end => context.CompletedNodes.Contains(end) || end == nodeId);
                if (allEndsDone)
                {
                    await context.StateLock.WaitAsync(ct).ConfigureAwait(false);
                    try
                    {
                        goalState.Status = GoalStatus.Achieved;
                        goalState.AchievedAt = _clock.GetUtcNow();
                    }
                    finally { context.StateLock.Release(); }
                    return goalState;
                }
            }
        }

        return goalState;
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
            NodeResult result = payload.Kind switch
            {
                GoalNodeKind.Agent => await ExecuteAgentNodeAsync(nodeId, payload, context, timeoutCts.Token).ConfigureAwait(false),
                GoalNodeKind.Function => await ExecuteFunctionNodeAsync(nodeId, payload, context, timeoutCts.Token).ConfigureAwait(false),
                GoalNodeKind.Join => await ExecuteJoinNodeAsync(nodeId, payload, context, timeoutCts.Token).ConfigureAwait(false),
                _ => NodeResult.Failed($"Unknown node kind: {payload.Kind}")
            };

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

    private async Task<NodeResult> ExecuteAgentNodeAsync(string nodeId, GoalNodePayload payload, GraphExecutionContext context, CancellationToken ct)
    {
        var agentId = payload.AgentId ?? Core.Agents.Coordinator.Agent.GenerateId();
        payload.AgentId = agentId;

        if (_agentRegistry.Get(new JoinCode.Abstractions.Entity.ObjectId(JoinCode.Abstractions.Entity.ObjectType.Agent, agentId)) is null)
        {
            _logger?.LogDebug("[GoalGraph] Agent {AgentId} 未在 Agent.Registry 中，将由 IAgentService 创建时自动注册", agentId);
        }

        var instruction = payload.Instruction ?? payload.Name;
        if (payload.Input is not null)
        {
            instruction = $"[上游输入]\n{payload.Input}\n\n[任务指令]\n{instruction}";
        }

        // === 完整模式：通过 IAgentService 执行（复用基础设施）===
        if (_agentService is not null && (payload.Role != default || payload.Variant.HasValue))
        {
            return await ExecuteViaAgentServiceAsync(nodeId, payload, instruction, context, ct).ConfigureAwait(false);
        }

        var missingReason = _agentService is null ? "IAgentService 未注入" : "Role/Variant 未指定";
        _logger?.LogError("[GoalGraph] {NodeId}({Name}): 无法执行 Agent 节点 — {Reason}。所有 Agent 节点必须通过 IAgentService 执行", nodeId, payload.Name, missingReason);
        return NodeResult.Failed($"Agent 节点无法执行: {missingReason}。Goal 模板必须为每个 agent 节点指定 Role/Variant");
    }

    /// <summary>
    /// 通过 IAgentService 执行 Agent 节点 — 复用完整基础设施
    /// （Transcript、MessageBroker、Worktree、Hook、MCP、Pause/Resume/Cancel）
    /// </summary>
    private async Task<NodeResult> ExecuteViaAgentServiceAsync(string nodeId, GoalNodePayload payload, string instruction, GraphExecutionContext context, CancellationToken ct)
    {
        var spawnOptions = new AgentSpawnOptions
        {
            Description = payload.Name,
            Prompt = instruction,
            Role = payload.Role,
            Variant = payload.Variant,
            RunInBackground = false,
            GoalId = context.State.GoalId,
            GraphNodeId = nodeId,
            TokenBudget = payload.TokenBudget,
            FreshContext = payload.FreshContext,
            SystemPrompt = payload.SystemPrompt,
        };

        var totalTokens = 0;
        var totalTurns = 0;
        var lastOutput = string.Empty;
        var responseBuilder = new System.Text.StringBuilder();

        await foreach (var chunk in _agentService!.RunAgentStreamAsync(spawnOptions, ct).ConfigureAwait(false))
        {
            if (chunk.Type == AgentStreamChunkType.Content)
            {
                responseBuilder.Append(chunk.Content);
            }
            else if (chunk.Type == AgentStreamChunkType.Complete)
            {
                totalTurns++;
                lastOutput = chunk.Content ?? responseBuilder.ToString();
                if (chunk.ExecutionTimeMs > 0)
                {
                    totalTokens += (int)chunk.ExecutionTimeMs;
                }
            }
        }

        payload.TokensUsed = totalTokens;
        payload.TurnsCompleted = totalTurns;

        if (!string.IsNullOrEmpty(lastOutput))
        {
            await context.StateLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                context.ChatHistory.AddAssistantMessage($"[{payload.Name}]: {lastOutput}");
            }
            finally { context.StateLock.Release(); }
        }

        return NodeResult.Succeeded(lastOutput, totalTokens);
    }




    private async Task<NodeResult> ExecuteFunctionNodeAsync(string nodeId, GoalNodePayload payload, GraphExecutionContext context, CancellationToken ct)
    {
        if (!_functionRegistry.TryGetValue(nodeId, out var fn))
        {
            return NodeResult.Failed($"Function not registered: {nodeId}");
        }

        var upstreamOutputs = context.CollectUpstreamOutputs(nodeId);
        var mutator = new GoalGraphMutator(context, _logger);
        var nodeContext = new NodeContext
        {
            NodeId = nodeId,
            CurrentNode = payload,
            UpstreamOutputs = upstreamOutputs,
            GlobalState = context.State,
            Services = _serviceProvider,
            CancellationToken = ct,
            GraphMutator = mutator,
        };

        return await fn(nodeContext).ConfigureAwait(false);
    }

    private sealed class GoalGraphMutator : IGoalGraphMutator
    {
        private readonly GraphExecutionContext _context;
        private readonly ILogger? _logger;

        public GoalGraphMutator(GraphExecutionContext context, ILogger? logger)
        {
            _context = context;
            _logger = logger;
        }

        public void AddNode(string nodeId, GoalNodePayload payload)
        {
            _context.Graph.Dag.AddNode(new DagNode<GoalNodePayload> { Id = nodeId, Payload = payload });
            _logger?.LogInformation("[GoalGraphMutator] 动态添加节点: {NodeId}", nodeId);
        }

        public void AddEdge(string edgeId, string fromId, string toId, string? label = null)
        {
            _context.Graph.Dag.AddEdge(new DagEdge { Id = edgeId, FromId = fromId, ToId = toId, Label = label ?? string.Empty });
            _logger?.LogInformation("[GoalGraphMutator] 动态添加边: {EdgeId} ({FromId} → {ToId})", edgeId, fromId, toId);
        }

        public void EnqueueNode(string nodeId)
        {
            _context.ReadyQueue.Enqueue(nodeId);
            _logger?.LogInformation("[GoalGraphMutator] 入队节点: {NodeId}", nodeId);
        }

        public void AddEndNode(string nodeId)
        {
            _context.Graph.AddEndNode(nodeId);
            _logger?.LogInformation("[GoalGraphMutator] 添加终止节点: {NodeId}", nodeId);
        }
    }

    private Task<NodeResult> ExecuteJoinNodeAsync(string nodeId, GoalNodePayload payload, GraphExecutionContext context, CancellationToken ct)
    {
        var upstreamOutputs = context.CollectUpstreamOutputs(nodeId);
        var totalUpstreams = context.CountTotalUpstreams(nodeId);
        var successfulUpstreams = context.CountSuccessfulUpstreams(nodeId);

        var minRequired = payload.MinSuccessfulInputs > 0
            ? payload.MinSuccessfulInputs
            : totalUpstreams;

        if (successfulUpstreams < minRequired)
        {
            return Task.FromResult(NodeResult.Failed(
                $"Join precondition not met: {successfulUpstreams}/{minRequired} upstreams succeeded ({totalUpstreams} total)"));
        }

        var failedUpstreams = totalUpstreams - successfulUpstreams;
        var sb = new System.Text.StringBuilder();

        foreach (var kvp in upstreamOutputs)
        {
            if (kvp.Value is not null)
            {
                sb.AppendLine($"[{kvp.Key}]: {kvp.Value}");
            }
            else
            {
                sb.AppendLine($"[{kvp.Key}]: <failed>");
            }
        }

        if (failedUpstreams > 0)
        {
            sb.AppendLine($"[warning]: {failedUpstreams} upstream(s) failed but Join proceeded (minRequired={minRequired})");
        }

        return Task.FromResult(NodeResult.Succeeded(sb.ToString().TrimEnd()));
    }

    private async Task HandleRetryAsync(string targetNodeId, GraphExecutionContext context, CancellationToken ct)
    {
        var retryCount = context.RetryCount.GetValueOrDefault(targetNodeId, 0);
        if (retryCount >= context.Graph.MaxRetriesPerNode)
        {
            _logger?.LogWarning("[GoalGraph] 回退超过最大重试次数: {NodeId} ({Retries}/{Max})",
                targetNodeId, retryCount, context.Graph.MaxRetriesPerNode);

            if (context.Graph.Dag.Nodes.TryGetValue(targetNodeId, out var node))
            {
                node.Payload.Status = GoalNodeStatus.Failed;
                node.Payload.ErrorMessage = $"Max retries ({context.Graph.MaxRetriesPerNode}) exceeded";
            }
            return;
        }

        var affected = context.Graph.Dag.GetAffectedSubgraph(targetNodeId);
        foreach (var node in affected)
        {
            node.Payload.Status = GoalNodeStatus.Pending;
            node.Payload.Output = null;
            node.Payload.Routes = null;
            node.Payload.ErrorMessage = null;
            node.Payload.StartedAt = null;
            node.Payload.CompletedAt = null;
            node.Payload.TokensUsed = 0;
            node.Version++;
            context.CompletedNodes.Remove(node.Id);
            context.FailedNodes.Remove(node.Id);
        }

        context.RetryCount[targetNodeId] = retryCount + 1;
        context.GlobalLoopIteration++;
        context.ReadyQueue.Enqueue(targetNodeId);

        _logger?.LogInformation("[GoalGraph] 回退重激活: {NodeId} (第{Retry}次, 影响{Count}个节点, 全局迭代={GlobalIter})",
            targetNodeId, retryCount + 1, affected.Count(), context.GlobalLoopIteration);
    }

    private async Task UpdateGoalStateAsync(GraphExecutionContext context)
    {
        var totalTokens = 0;
        var totalTurns = 0;
        foreach (var node in context.Graph.Dag.Nodes.Values)
        {
            totalTokens += node.Payload.TokensUsed;
            if (node.Payload.Status == GoalNodeStatus.Completed)
                totalTurns++;
        }

        await context.StateLock.WaitAsync().ConfigureAwait(false);
        try
        {
            context.State.TokensUsed = totalTokens;
            context.State.TurnsCompleted = totalTurns;
        }
        finally { context.StateLock.Release(); }
    }

    /// <summary>
    /// 负向评价循环中的用户权限询问
    /// 负评6~10条时，ask_user 询问用户是否继续
    /// 1分钟超时后协调者自动接管（用户可能睡觉/离开）
    /// </summary>
    private async Task HandleUserInteractionAsync(string nodeId, GoalNodePayload payload, GraphExecutionContext context, CancellationToken ct)
    {
        if (_userInteraction is null)
            return;

        if (payload.NegativeReviewCount < 6 || payload.NegativeReviewCount > 10)
            return;

        var decision = await _userInteraction.AskToContinueAsync(
            $"负向评价发现 {payload.NegativeReviewCount} 条不足，是否继续循环修复？",
            payload.NegativeReviewCount,
            context.GlobalLoopIteration,
            timeoutSeconds: 60,
            cancellationToken: ct).ConfigureAwait(false);

        if (decision.CoordinatorTakenOver)
        {
            context.CoordinatorTerminated = true;
            _logger?.LogWarning("[GoalGraph] 协调者接管: {Reason} (节点={NodeId}, 负评={NegCount})",
                decision.Reason, nodeId, payload.NegativeReviewCount);
            return;
        }

        if (!decision.ShouldContinue)
        {
            payload.Routes = new[] { "NEG_STOP" };
            _logger?.LogInformation("[GoalGraph] 用户选择停止循环 (节点={NodeId}, 负评={NegCount})",
                nodeId, payload.NegativeReviewCount);
        }
    }

    /// <summary>
    /// 协调者窥探 — 观察循环状态，决定是否终止
    /// </summary>
    private async Task HandleLoopObservationAsync(string nodeId, GoalNodePayload payload, GraphExecutionContext context, CancellationToken ct)
    {
        if (_loopObserver is null)
            return;

        if (!nodeId.Equals("neg_review", StringComparison.Ordinal) && !nodeId.Equals("fix_neg", StringComparison.Ordinal))
            return;

        var observationContext = new LoopObservationContext
        {
            GoalId = context.State.GoalId,
            NodeId = nodeId,
            LoopIteration = context.GlobalLoopIteration,
            NegativeReviewCount = payload.NegativeReviewCount,
            TotalTokensConsumed = context.TotalTokensConsumed,
            TotalTurnsCompleted = context.State.TurnsCompleted,
            LastNodeOutput = payload.Output,
            NegativeReviewTaskId = payload.NegativeReviewTaskId,
        };

        var shouldTerminate = await _loopObserver.ObserveAsync(observationContext, ct).ConfigureAwait(false);

        if (shouldTerminate)
        {
            context.CoordinatorTerminated = true;
            _logger?.LogInformation("[GoalGraph] 协调者窥探终止: 节点={NodeId}, 迭代={Iter}, 负评={NegCount}",
                nodeId, context.GlobalLoopIteration, payload.NegativeReviewCount);
        }
    }

    /// <summary>
    /// 从 neg_review / fix_neg 节点输出中提取 JSON 元数据并写入 payload
    /// 使用 LlmJsonHelper 统一门控（ExtractJsonBlock + RepairJson + 宽容反序列化）
    /// </summary>
    private static void ExtractNegReviewMetadata(string nodeId, GoalNodePayload payload, GraphExecutionContext context)
    {
        if (string.IsNullOrEmpty(payload.Output))
            return;

        if (nodeId.Equals("neg_review", StringComparison.Ordinal))
        {
            var negReview = LlmJsonHelper.Deserialize(payload.Output, GoalJsonContext.Default.NegReviewOutputJson, out var negRepair);
            if (negReview is null)
            {
                if (!string.IsNullOrEmpty(negRepair))
                    System.Diagnostics.Trace.WriteLine($"[GoalGraph] neg_review 元数据解析失败: {negRepair}");
                return;
            }

            payload.NegativeReviewCount = negReview.NegativeReviewCount;
            payload.NegativeReviewTaskId = negReview.TaskId;
            if (!string.IsNullOrEmpty(negReview.Route))
            {
                payload.Routes = [negReview.Route];
            }
        }
        else if (nodeId.Equals("fix_neg", StringComparison.Ordinal))
        {
            var fixNeg = LlmJsonHelper.Deserialize(payload.Output, GoalJsonContext.Default.FixNegOutputJson, out var fixRepair);
            if (fixNeg is not null && !string.IsNullOrEmpty(fixNeg.Route))
            {
                payload.Routes = [fixNeg.Route];
            }
            else if (fixNeg is null && !string.IsNullOrEmpty(fixRepair))
            {
                System.Diagnostics.Trace.WriteLine($"[GoalGraph] fix_neg 元数据解析失败: {fixRepair}");
            }
        }
    }

    /// <summary>
    /// 判断是否应终止负向评价-修复循环
    /// 终止条件（纵深防御，任一满足即终止）:
    /// 1. 协调者终止（窥探或接管）
    /// 2. 循环迭代达到硬上限（默认16轮）
    /// 3. token/轮次预算耗尽
    /// </summary>
    private static bool ShouldTerminateLoop(string nodeId, GoalNodePayload payload, GraphExecutionContext context, GoalGraph graph)
    {
        if (context.CoordinatorTerminated)
            return true;

        if (context.GlobalLoopIteration >= graph.HardMaxLoopIterations)
            return true;

        if (context.State.TokenBudget.HasValue && context.TotalTokensConsumed >= context.State.TokenBudget.Value)
            return true;

        if (context.State.TurnBudget.HasValue && context.GlobalLoopIteration >= context.State.TurnBudget.Value)
            return true;

        return false;
    }
}
