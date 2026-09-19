namespace Core.Goal;

/// <summary>
/// 节点执行器接口 — 策略模式，按 GoalNodeKind 分发到具体执行器。
/// <para>从 GoalGraphEngine 提取，每种节点类型（Agent/Function/Join）有独立实现。</para>
/// </summary>
internal interface INodeExecutor {
    /// <summary>节点类型</summary>
    GoalNodeKind Kind { get; }

    /// <summary>异步执行节点，返回执行结果</summary>
    /// <param name="nodeId">节点 ID</param>
    /// <param name="payload">节点负载</param>
    /// <param name="context">图执行上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>节点执行结果</returns>
    Task<NodeResult> ExecuteAsync(string nodeId, GoalNodePayload payload, GraphExecutionContext context, CancellationToken ct);
}

/// <summary>
/// Agent 节点执行器 — 通过 IAgentService 执行，复用完整基础设施。
/// <para>职责：生成 AgentId → 构造指令 → 热点文件检查 → 流式执行 → 团队加入 → 聊天历史追加。</para>
/// </summary>
internal sealed class AgentNodeExecutor : INodeExecutor {
    private readonly IAgentService? _agentService;
    private readonly ICaptainDispatchGuard? _dispatchGuard;
    private readonly ITeamManager? _teamManager;
    private readonly GoalStateUpdater _stateUpdater;
    private readonly ILogger? _logger;

    /// <summary>初始化 Agent 节点执行器</summary>
    public AgentNodeExecutor(
        IAgentService? agentService,
        ICaptainDispatchGuard? dispatchGuard,
        ITeamManager? teamManager,
        GoalStateUpdater stateUpdater,
        ILogger? logger) {
        _agentService = agentService;
        _dispatchGuard = dispatchGuard;
        _teamManager = teamManager;
        _stateUpdater = stateUpdater;
        _logger = logger;
    }

    /// <inheritdoc/>
    public GoalNodeKind Kind => GoalNodeKind.Agent;

    /// <inheritdoc/>
    public async Task<NodeResult> ExecuteAsync(string nodeId, GoalNodePayload payload, GraphExecutionContext context, CancellationToken ct) {
        var agentId = payload.AgentId ?? Core.Agents.Coordinator.AgentBase.GenerateId();
        payload.AgentId = agentId;

        if (Core.Agents.Coordinator.AgentBase.GetById(new JoinCode.Abstractions.Entity.ObjectId(JoinCode.Abstractions.Entity.ObjectType.Agent, agentId)) is null) {
            _logger?.LogDebug("[GoalGraph] Agent {AgentId} 未在 SessionScope 中，将由 IAgentService 创建时自动注册", agentId);
        }

        var instruction = payload.Instruction ?? payload.Name;
        if (payload.Input is not null) {
            instruction = $"[上游输入]\n{payload.Input}\n\n[任务指令]\n{instruction}";
        }

        if (_agentService is not null) {
            return await ExecuteViaAgentServiceAsync(nodeId, payload, instruction, context, ct).ConfigureAwait(false);
        }

        _logger?.LogError("[GoalGraph] {NodeId}({Name}): 无法执行 Agent 节点 — IAgentService 未注入。所有 Agent 节点必须通过 IAgentService 执行", nodeId, payload.Name);
        return NodeResult.Failed("Agent 节点无法执行: IAgentService 未注入。Goal 模板必须为每个 agent 节点指定 Role/Variant");
    }

    /// <summary>通过 IAgentService 执行 Agent 节点 — 复用完整基础设施</summary>
    private async Task<NodeResult> ExecuteViaAgentServiceAsync(string nodeId, GoalNodePayload payload, string instruction, GraphExecutionContext context, CancellationToken ct) {
        var spawnOptions = new AgentSpawnOptions {
            Description = payload.Name,
            Prompt = instruction,
            Role = payload.Role,
            Variant = payload.Variant,
            IsolationMode = payload.IsolationMode,
            RunInBackground = false,
            GoalId = context.State.GoalId,
            GraphNodeId = nodeId,
            TokenBudget = payload.TokenBudget,
            FreshContext = payload.FreshContext,
            SystemPrompt = payload.SystemPrompt,
        };

        if (_dispatchGuard is not null && payload.OwnedFiles is { Length: > 0 }) {
            var decision = _dispatchGuard.CheckBeforeDispatch(payload.OwnedFiles);
            if (decision.ShouldCaptainHandle) {
                _logger?.LogInformation("[GoalGraph] {NodeId}({Name}): 热点文件契约改，队长自己揽 — {Reason}，热点文件: {Files}",
                    nodeId, payload.Name, decision.Reason, string.Join(", ", decision.HotSpotFiles));
                spawnOptions = spawnOptions with { Role = AgentRole.Coordinator };
            }
        }

        var totalTokens = 0;
        var totalTurns = 0;
        var lastOutput = string.Empty;
        var responseBuilder = new System.Text.StringBuilder();
        var teamMemberAdded = false;

        await foreach (var chunk in _agentService!.RunAgentStreamAsync(spawnOptions, ct).ConfigureAwait(false)) {
            if (!teamMemberAdded && context.TeamId is not null && !string.IsNullOrEmpty(chunk.AgentId)) {
                teamMemberAdded = true;
                try {
                    await _teamManager!.AddTeamMemberAsync(context.TeamId, chunk.AgentId, ct).ConfigureAwait(false);
                } catch (Exception ex) {
                    _logger?.LogWarning("[GoalGraph] 加入团队失败: {Message}", ex.Message);
                }
            }

            if (chunk.Type == AgentStreamChunkType.Content) {
                responseBuilder.Append(chunk.Content);
            } else if (chunk.Type == AgentStreamChunkType.Complete) {
                totalTurns++;
                lastOutput = chunk.Content ?? responseBuilder.ToString();
                if (chunk.ExecutionTimeMs > 0) {
                    totalTokens += (int)chunk.ExecutionTimeMs;
                }
            }
        }

        payload.TokensUsed = totalTokens;
        payload.TurnsCompleted = totalTurns;

        if (!string.IsNullOrEmpty(lastOutput)) {
            await _stateUpdater.AppendChatMessageAsync(context, $"[{payload.Name}]: {lastOutput}", ct).ConfigureAwait(false);
        }

        return NodeResult.Succeeded(lastOutput, totalTokens);
    }
}

/// <summary>
/// Function 节点执行器 — 调用注册的自定义委托。
/// <para>职责：查找已注册函数 → 构造 NodeContext → 调用委托。</para>
/// </summary>
internal sealed class FunctionNodeExecutor : INodeExecutor {
    private readonly Dictionary<string, Func<NodeContext, Task<NodeResult>>> _functionRegistry;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger? _logger;

    /// <summary>初始化 Function 节点执行器</summary>
    public FunctionNodeExecutor(
        Dictionary<string, Func<NodeContext, Task<NodeResult>>> functionRegistry,
        IServiceProvider serviceProvider,
        ILogger? logger) {
        _functionRegistry = functionRegistry;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public GoalNodeKind Kind => GoalNodeKind.Function;

    /// <inheritdoc/>
    public async Task<NodeResult> ExecuteAsync(string nodeId, GoalNodePayload payload, GraphExecutionContext context, CancellationToken ct) {
        if (!_functionRegistry.TryGetValue(nodeId, out var fn)) {
            return NodeResult.Failed($"Function not registered: {nodeId}");
        }

        var upstreamOutputs = context.CollectUpstreamOutputs(nodeId);
        var mutator = new GoalGraphMutator(context, _logger);
        var nodeContext = new NodeContext {
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
}

/// <summary>
/// Join 节点执行器 — 汇聚上游输出，检查最小成功数。
/// <para>职责：收集上游输出 → 检查 Join 前置条件 → 拼接输出文本。</para>
/// </summary>
internal sealed class JoinNodeExecutor : INodeExecutor {
    /// <inheritdoc/>
    public GoalNodeKind Kind => GoalNodeKind.Join;

    /// <inheritdoc/>
    public Task<NodeResult> ExecuteAsync(string nodeId, GoalNodePayload payload, GraphExecutionContext context, CancellationToken ct) {
        var upstreamOutputs = context.CollectUpstreamOutputs(nodeId);
        var totalUpstreams = context.CountTotalUpstreams(nodeId);
        var successfulUpstreams = context.CountSuccessfulUpstreams(nodeId);

        var minRequired = payload.MinSuccessfulInputs > 0
            ? payload.MinSuccessfulInputs
            : totalUpstreams;

        if (successfulUpstreams < minRequired) {
            return Task.FromResult(NodeResult.Failed(
                $"Join precondition not met: {successfulUpstreams}/{minRequired} upstreams succeeded ({totalUpstreams} total)"));
        }

        var failedUpstreams = totalUpstreams - successfulUpstreams;
        var sb = new System.Text.StringBuilder();

        foreach (var kvp in upstreamOutputs) {
            if (kvp.Value is not null) {
                sb.AppendLine($"[{kvp.Key}]: {kvp.Value}");
            } else {
                sb.AppendLine($"[{kvp.Key}]: <failed>");
            }
        }

        if (failedUpstreams > 0) {
            sb.AppendLine($"[warning]: {failedUpstreams} upstream(s) failed but Join proceeded (minRequired={minRequired})");
        }

        return Task.FromResult(NodeResult.Succeeded(sb.ToString().TrimEnd()));
    }
}

/// <summary>
/// 节点执行器分发器 — 按 GoalNodeKind 分发到对应 INodeExecutor。
/// <para>从 GoalGraphEngine 提取，消除 Engine 对具体执行逻辑的直接依赖。</para>
/// <para>职责单一：按 Kind 查表分发，不包含任何执行逻辑。</para>
/// </summary>
internal sealed class NodeExecutorDispatcher {
    private readonly FrozenDictionary<GoalNodeKind, INodeExecutor> _executors;

    /// <summary>初始化节点执行器分发器</summary>
    /// <param name="executors">节点执行器列表</param>
    public NodeExecutorDispatcher(IEnumerable<INodeExecutor> executors) {
        _executors = executors.ToFrozenDictionary(e => e.Kind);
    }

    /// <summary>按节点类型分发执行</summary>
    /// <param name="nodeId">节点 ID</param>
    /// <param name="payload">节点负载</param>
    /// <param name="context">图执行上下文</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>节点执行结果</returns>
    public Task<NodeResult> ExecuteAsync(string nodeId, GoalNodePayload payload, GraphExecutionContext context, CancellationToken ct) {
        if (!_executors.TryGetValue(payload.Kind, out var executor))
            return Task.FromResult(NodeResult.Failed($"Unknown node kind: {payload.Kind}"));
        return executor.ExecuteAsync(nodeId, payload, context, ct);
    }
}