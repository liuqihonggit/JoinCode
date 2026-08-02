namespace Core.Goal;

using JoinCode.Abstractions.Models.Goal;
using JoinCode.Abstractions.LLM;
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
    private readonly Dictionary<string, Func<NodeContext, Task<NodeResult>>> _functionRegistry = new(StringComparer.Ordinal);

    public GoalGraphEngine(
        IChatClient kernel,
        IGoalEvaluator evaluator,
        IServiceProvider serviceProvider,
        ILogger<GoalGraphEngine>? logger = null,
        IGoalHeartbeat? heartbeat = null,
        IClockService? clock = null)
    {
        _kernel = kernel;
        _evaluator = evaluator;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _heartbeat = heartbeat ?? new GoalHeartbeat();
        _clock = clock ?? SystemClockService.Instance;
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
        var chatHistory = new MessageList();
        foreach (var msg in context.ChatHistory)
        {
            chatHistory.Add(msg);
        }

        if (payload.SystemPrompt is not null)
        {
            chatHistory.AddSystemMessage(payload.SystemPrompt);
        }

        var instruction = payload.Instruction ?? payload.Name;
        if (payload.Input is not null)
        {
            instruction = $"[上游输入]\n{payload.Input}\n\n[任务指令]\n{instruction}";
        }
        chatHistory.AddUserMessage(instruction);

        var chatService = _kernel.GetChatCompletionService();
        var executionSettings = new ChatOptions
        {
            Temperature = 0.7f,
            MaxTokens = 8000,
            ToolChoice = ToolChoice.AutoInvoke
        };

        var totalTokens = 0;
        var totalTurns = 0;
        var lastOutput = string.Empty;

        while (!ct.IsCancellationRequested)
        {
            if (payload.TokenBudget is { } budget && totalTokens >= budget)
            {
                _logger?.LogWarning("[GoalGraph] {NodeId}({Name}): Token预算耗尽 ({Used}/{Budget})",
                    nodeId, payload.Name, totalTokens, budget);
                break;
            }

            var results = await chatService.GetApiMessageContentsAsync(
                chatHistory,
                executionSettings,
                _kernel,
                ct).ConfigureAwait(false);

            var outputText = results.Count > 0 ? results[0].Content ?? string.Empty : string.Empty;
            var tokensUsed = results.Count > 0 && results[0].TokenUsage is { TotalTokens: var tt }
                ? tt
                : 0;

            totalTokens += tokensUsed;
            totalTurns++;
            lastOutput = outputText;
            payload.TokensUsed = totalTokens;
            payload.TurnsCompleted = totalTurns;

            if (!string.IsNullOrEmpty(outputText))
            {
                chatHistory.AddAssistantMessage(outputText);
            }

            var evaluation = await _evaluator.EvaluateAsync(
                instruction,
                [],
                outputText,
                ct).ConfigureAwait(false);

            if (evaluation.IsCompleted)
            {
                _logger?.LogInformation("[GoalGraph] {NodeId}({Name}): Agent循环完成 (turns={Turns}, tokens={Tokens})",
                    nodeId, payload.Name, totalTurns, totalTokens);
                break;
            }

            var continuationPrompt = ContinuationPromptBuilder.BuildContinuationPrompt(
                instruction,
                [],
                totalTokens,
                payload.TokenBudget,
                evaluation.Reason);
            chatHistory.AddSystemMessage(continuationPrompt);

            _logger?.LogDebug("[GoalGraph] {NodeId}({Name}): Agent继续 (turns={Turns})", nodeId, payload.Name, totalTurns);
        }

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
        var nodeContext = new NodeContext
        {
            NodeId = nodeId,
            CurrentNode = payload,
            UpstreamOutputs = upstreamOutputs,
            GlobalState = context.State,
            Services = _serviceProvider,
            CancellationToken = ct,
        };

        return await fn(nodeContext).ConfigureAwait(false);
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
        context.ReadyQueue.Enqueue(targetNodeId);

        _logger?.LogInformation("[GoalGraph] 回退重激活: {NodeId} (第{Retry}次, 影响{Count}个节点)",
            targetNodeId, retryCount + 1, affected.Count);
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
}
