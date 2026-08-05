
namespace Core.Goal;

using System.Collections.Frozen;
using Structura.Dag;

// IGoalEngine 接口已移至 JoinCode.Abstractions.Interfaces.Scheduling

[Register]
public sealed partial class GoalEngine : IGoalEngine, IAsyncDisposable
{
    private readonly IChatClient _kernel;
    private readonly IGoalEvaluator _evaluator;
    private readonly IGoalHeartbeat _heartbeat;
    private readonly SemaphoreSlim _stateLock;
    [Inject] private readonly ILogger<GoalEngine>? _logger;
    [Inject] private readonly IClockService _clock;
    [Inject] private readonly IServiceProvider _serviceProvider = null!;
    [Inject] private readonly IGoalGraphTemplateRegistry _templateRegistry = null!;
    private Core.Agents.Coordinator.AgentRegistry _agentRegistry => Core.Agents.Coordinator.Agent.Registry;
    private readonly IToolPermissionManager? _permissionManager;
    private readonly MiddlewarePipeline<GoalLifecycleContext>? _lifecyclePipeline;
    private GoalState? _state;
    private CancellationTokenSource? _engineCts;
    private Task? _engineLoop;
    private int _goalCounter;
    private PermissionMode? _savedPermissionMode;
    private readonly MessageList _chatHistory;
    private TaskCompletionSource? _completionTcs;
    private GoalGraph? _goalGraph;
    private GoalGraphEngine? _graphEngine;

    public GoalState? CurrentState => _state;
    public bool IsRunning => _state?.Status == GoalStatus.Pursuing;
    public bool HasGraphDefinition => _goalGraph is not null;

    /// <summary>
    /// 等待目标引擎循环退出（完成、预算耗尽、暂停、清除等）。
    /// </summary>
    public Task WaitForCompletionAsync(CancellationToken ct = default)
    {
        return _completionTcs?.Task ?? Task.CompletedTask;
    }

    /// <summary>
    /// 设置 Graph 定义 — 由协调者 Agent 通过 goal_graph_define MCP 工具调用
    /// </summary>
    public void SetGraphDefinition(string nodesJson, string edgesJson, string startNodeId, string endNodeIds)
    {
        ArgumentNullException.ThrowIfNull(nodesJson);
        ArgumentNullException.ThrowIfNull(edgesJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(startNodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(endNodeIds);

        if (_goalGraph is not null)
        {
            _logger?.LogWarning("[GoalEngine] Graph 已存在，忽略重复定义");
            return;
        }

        _graphEngine = new GoalGraphEngine(
            _kernel,
            _evaluator,
            _serviceProvider,
            logger: null,
            heartbeat: _heartbeat,
            clock: _clock);

        var dag = new Dag<GoalNodePayload>();

        var nodes = LlmJsonHelper.DeserializeValue(nodesJson, GraphDefineJsonContext.Default.GraphDefineNodeArray, out var nodesRepair)
            ?? throw new ArgumentException(FormatInvalidGraphError("nodes", nodesRepair));

        foreach (var node in nodes)
        {
            var nodeId = node.Id ?? throw new ArgumentException("Node id is required");
            dag.AddNode(new DagNode<GoalNodePayload>
            {
                Id = nodeId,
                Payload = new GoalNodePayload
                {
                    Kind = node.Kind?.ToLowerInvariant() switch
                    {
                        "function" => GoalNodeKind.Function,
                        "join" => GoalNodeKind.Join,
                        _ => GoalNodeKind.Agent,
                    },
                    Name = node.Name ?? nodeId,
                    Role = AgentRole.Executor,
                    SystemPrompt = node.SystemPrompt,
                    Instruction = node.Instruction,
                    FreshContext = node.FreshContext,
                },
            });
        }

        var edges = LlmJsonHelper.DeserializeValue(edgesJson, GraphDefineJsonContext.Default.GraphDefineEdgeArray, out var edgesRepair)
            ?? throw new ArgumentException(FormatInvalidGraphError("edges", edgesRepair));

        foreach (var edge in edges)
        {
            var fromId = edge.FromId ?? throw new ArgumentException("Edge fromId is required");
            var toId = edge.ToId ?? throw new ArgumentException("Edge toId is required");
            var edgeId = edge.Id ?? $"e-{fromId}-{toId}";

            var result = dag.TryAddEdge(new DagEdge
            {
                Id = edgeId,
                FromId = fromId,
                ToId = toId,
                Label = edge.Label ?? string.Empty,
            });

            if (!result.Success && edge.Label?.Length > 0)
            {
                if (dag.Nodes.TryGetValue(toId, out var targetNode))
                {
                    targetNode.InEdgeIds.Remove(edgeId);
                }
            }
        }

        var endSet = endNodeIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _goalGraph = new GoalGraph
        {
            Name = _state?.Objective ?? "dynamic-graph",
            Dag = dag,
            StartNodeId = startNodeId,
            EndNodeIds = endSet.ToFrozenSet(StringComparer.Ordinal),
        };

        _logger?.LogInformation("[GoalEngine] 协调者定义了 Graph: {NodeCount}个节点, {EdgeCount}条边, Start={Start}",
            nodes.Length, edges.Length, startNodeId);
    }

    /// <summary>
    /// 将 Graph JSON 解析失败信息连同修复/宽容明细组装为精确错误消息，供 MCP 工具结果回喂 LLM。
    /// </summary>
    private static string FormatInvalidGraphError(string kind, string? repairHint)
    {
        var message = $"Invalid {kind} JSON";
        if (!string.IsNullOrEmpty(repairHint))
            message = $"{message}: {repairHint}";
        return message;
    }

    public GoalEngine(
        IChatClient kernel,
        IGoalEvaluator evaluator,
        ILogger<GoalEngine>? logger = null,
        ILoggerFactory? loggerFactory = null,
        IToolPermissionManager? permissionManager = null,
        IEnumerable<IGoalLifecycleMiddleware>? lifecycleMiddlewares = null,
        IGoalHeartbeat? heartbeat = null,
        IClockService? clock = null,
        IServiceProvider? serviceProvider = null)
    {
        _kernel = kernel;
        _evaluator = evaluator;
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _permissionManager = permissionManager;
        _serviceProvider = serviceProvider ?? _serviceProvider;
        _stateLock = new SemaphoreSlim(1, 1);
        _chatHistory = new MessageList();
        _heartbeat = heartbeat ?? throw new ArgumentNullException(nameof(heartbeat));
        _heartbeat.RegisterCallback(OnHeartbeatAsync);

        if (lifecycleMiddlewares is not null && loggerFactory is not null)
        {
            _lifecyclePipeline = new PipelineBuilder<GoalLifecycleContext>()
                .WithLoggingScope(loggerFactory)
                .UseRange(lifecycleMiddlewares)
                .Build();
        }
        else if (lifecycleMiddlewares is not null)
        {
            _lifecyclePipeline = new MiddlewarePipeline<GoalLifecycleContext>(lifecycleMiddlewares);
        }
    }

    public async Task<GoalState> StartAsync(
        string objective,
        List<string>? constraints = null,
        int? tokenBudget = null,
        string? systemPrompt = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(objective);

        BuildDefaultGraphIfAbsent(objective, tokenBudget);

        if (_lifecyclePipeline is not null)
        {
            return await StartViaPipelineAsync(objective, constraints, tokenBudget, systemPrompt, cancellationToken).ConfigureAwait(false);
        }

        return await StartDirectAsync(objective, constraints, tokenBudget, systemPrompt, cancellationToken).ConfigureAwait(false);
    }

    private void BuildDefaultGraphIfAbsent(string objective, int? tokenBudget)
    {
        if (_goalGraph is not null && _graphEngine is not null)
            return;

        ArgumentNullException.ThrowIfNull(_serviceProvider);

        _graphEngine = new GoalGraphEngine(
            _kernel,
            _evaluator,
            _serviceProvider,
            logger: null,
            heartbeat: _heartbeat,
            clock: _clock);

        var template = _templateRegistry?.FindMatch(objective);
        if (template is not null)
        {
            _goalGraph = template.BuildGraph(_graphEngine, objective);
            _logger?.LogInformation("[GoalEngine] 匹配到 Graph 模板: {TemplateName} → {GraphName}", template.Name, _goalGraph.Name);
            return;
        }

        var dag = new Dag<GoalNodePayload>();

        dag.AddNode(new DagNode<GoalNodePayload>
        {
            Id = "agent",
            Payload = new GoalNodePayload
            {
                Kind = GoalNodeKind.Agent,
                Name = "executor",
                Role = AgentRole.Executor,
                Instruction = objective,
                TokenBudget = tokenBudget,
            },
        });

        dag.AddNode(new DagNode<GoalNodePayload>
        {
            Id = "reviewer",
            Payload = new GoalNodePayload
            {
                Kind = GoalNodeKind.Agent,
                Name = "reviewer",
                Role = AgentRole.Coordinator,
                SystemPrompt = "You are an independent reviewer. Evaluate the following work output objectively. You must determine if the task was completed successfully. Reply with PASS if the work meets the requirements, or FAIL with specific issues if it does not. Do not assume context you were not given — judge only by what you see.",
                Instruction = "Review the following work output and determine if it successfully completes the task. Be objective and thorough.",
                FreshContext = true,
                TokenBudget = tokenBudget.HasValue ? tokenBudget.Value / 4 : null,
            },
        });

        dag.AddEdge(new DagEdge { Id = "e-agent-reviewer", FromId = "agent", ToId = "reviewer" });

        const string backEdgeId = "e-reviewer-agent";
        dag.TryAddEdge(new DagEdge { Id = backEdgeId, FromId = "reviewer", ToId = "agent", Label = "FAIL" });
        dag.Nodes["agent"].InEdgeIds.Remove(backEdgeId);

        _goalGraph = new GoalGraph
        {
            Name = objective,
            Dag = dag,
            StartNodeId = "agent",
            EndNodeIds = FrozenSet.Create("reviewer"),
        };

        _logger?.LogInformation("[GoalEngine] 自动构建 agent→reviewer Graph: {Objective}", objective);
    }

    private async Task<GoalState> StartViaPipelineAsync(
        string objective,
        List<string>? constraints,
        int? tokenBudget,
        string? systemPrompt,
        CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != null && _state.Status == GoalStatus.Pursuing)
            {
                throw new InvalidOperationException(L.T(StringKey.GoalEngineAlreadyRunning));
            }

            var goalId = GenerateGoalId();
            _state = new GoalState
            {
                GoalId = goalId,
                Objective = objective,
                Status = GoalStatus.Pursuing,
                Constraints = constraints ?? [],
                TokenBudget = tokenBudget
            };

            _chatHistory.Clear();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                _chatHistory.AddSystemMessage(systemPrompt);
            _chatHistory.AddUserMessage(objective);
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.Start,
            Objective = objective,
            Constraints = constraints,
            TokenBudget = tokenBudget,
            CancellationToken = cancellationToken,
            State = _state,
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
            SavedPermissionMode = _savedPermissionMode,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is null)
        {
            return _state;
        }

        await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);

        _savedPermissionMode = ctx.SavedPermissionMode;

        RegisterMainAgent(_state.GoalId, objective, tokenBudget);

        if (ctx.ShouldStartEngineLoop)
        {
            _logger?.LogInformation(L.T(StringKey.GoalEngineStarting),
                _state.GoalId, objective, tokenBudget?.ToString() ?? L.T(StringKey.GoalEngineBudgetUnlimited));

            _engineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _completionTcs = new();
            _engineLoop = Task.Run(() => RunGoalLoopAsync(_engineCts.Token));
        }

        return _state;
    }

    private async Task<GoalState> StartDirectAsync(
        string objective,
        List<string>? constraints,
        int? tokenBudget,
        string? systemPrompt,
        CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state != null && _state.Status == GoalStatus.Pursuing)
            {
                throw new InvalidOperationException(L.T(StringKey.GoalEngineAlreadyRunning));
            }

            var goalId = GenerateGoalId();
            _state = new GoalState
            {
                GoalId = goalId,
                Objective = objective,
                Status = GoalStatus.Pursuing,
                Constraints = constraints ?? [],
                TokenBudget = tokenBudget
            };

            _chatHistory.Clear();
            if (!string.IsNullOrWhiteSpace(systemPrompt))
                _chatHistory.AddSystemMessage(systemPrompt);
            _chatHistory.AddUserMessage(objective);
        }
        finally
        {
            _stateLock.Release();
        }

        await SwitchToGoalPermissionModeAsync(cancellationToken).ConfigureAwait(false);

        RegisterMainAgent(_state.GoalId, objective, tokenBudget);

        _logger?.LogInformation(L.T(StringKey.GoalEngineStarting),
            _state.GoalId, objective, tokenBudget?.ToString() ?? L.T(StringKey.GoalEngineBudgetUnlimited));

        _engineCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _completionTcs = new();
        _engineLoop = Task.Run(() => RunGoalLoopAsync(_engineCts.Token));

        return _state;
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (_lifecyclePipeline is not null)
        {
            await PauseViaPipelineAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await PauseDirectAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task PauseViaPipelineAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Paused;
            _state.PausedAt = _clock.GetUtcNow();
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.Pause,
            CancellationToken = cancellationToken,
            State = _state ?? new GoalState { GoalId = string.Empty, Objective = string.Empty, Status = GoalStatus.Paused },
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        if (ctx.ShouldResetHeartbeat)
        {
            await _heartbeat.ResetAsync().ConfigureAwait(false);
        }

        _logger?.LogInformation(L.T(StringKey.GoalEnginePaused), _state?.GoalId);
    }

    private async Task PauseDirectAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Paused;
            _state.PausedAt = _clock.GetUtcNow();
        }
        finally
        {
            _stateLock.Release();
        }

        await _heartbeat.ResetAsync().ConfigureAwait(false);
        _logger?.LogInformation(L.T(StringKey.GoalEnginePaused), _state?.GoalId);
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (_lifecyclePipeline is not null)
        {
            await ResumeViaPipelineAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await ResumeDirectAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ResumeViaPipelineAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Paused) return;

            _state.Status = GoalStatus.Pursuing;
            _state.PausedAt = null;
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.Resume,
            CancellationToken = cancellationToken,
            State = _state ?? new GoalState { GoalId = string.Empty, Objective = string.Empty, Status = GoalStatus.Pursuing },
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        if (ctx.ShouldStartEngineLoop)
        {
            var continuationPrompt = ContinuationPromptBuilder.BuildContinuationPrompt(
                _state?.Objective ?? throw new InvalidOperationException("GoalState is not initialized."),
                _state.Constraints,
                _state.TokensUsed,
                _state.TokenBudget,
                _state.LastEvaluation?.Reason ?? L.T(StringKey.GoalEngineUserResumeReason));

            _chatHistory.AddSystemMessage(continuationPrompt);

            _engineCts?.Cancel();
            _engineCts?.Dispose();
            _engineCts = new CancellationTokenSource();
            _completionTcs = new();
            _engineLoop = Task.Run(() => RunGoalLoopAsync(_engineCts.Token));
        }

        _logger?.LogInformation(L.T(StringKey.GoalEngineResumed), _state?.GoalId);
    }

    private async Task ResumeDirectAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Paused) return;

            _state.Status = GoalStatus.Pursuing;
            _state.PausedAt = null;
        }
        finally
        {
            _stateLock.Release();
        }

        var continuationPrompt = ContinuationPromptBuilder.BuildContinuationPrompt(
            _state?.Objective ?? throw new InvalidOperationException("GoalState is not initialized."),
            _state.Constraints,
            _state.TokensUsed,
            _state.TokenBudget,
            _state.LastEvaluation?.Reason ?? L.T(StringKey.GoalEngineUserResumeReason));

        _chatHistory.AddSystemMessage(continuationPrompt);

        _engineCts?.Cancel();
        _engineCts?.Dispose();
        _engineCts = new CancellationTokenSource();
        _completionTcs = new();
        _engineLoop = Task.Run(() => RunGoalLoopAsync(_engineCts.Token));
        _logger?.LogInformation(L.T(StringKey.GoalEngineResumed), _state?.GoalId);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        if (_lifecyclePipeline is not null)
        {
            await ClearViaPipelineAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await ClearDirectAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ClearViaPipelineAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null) return;

            _state.Status = GoalStatus.Unmet;
            _state.AchievedAt = _clock.GetUtcNow();
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.Clear,
            CancellationToken = cancellationToken,
            State = _state ?? new GoalState { GoalId = string.Empty, Objective = string.Empty, Status = GoalStatus.Unmet },
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
            SavedPermissionMode = _savedPermissionMode,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        _savedPermissionMode = ctx.SavedPermissionMode;

        if (ctx.ShouldCancelEngineLoop)
        {
            _engineCts?.Cancel();
        }

        if (ctx.ShouldResetHeartbeat)
        {
            await _heartbeat.ResetAsync().ConfigureAwait(false);
        }

        _logger?.LogInformation(L.T(StringKey.GoalEngineCleared), _state?.GoalId);
    }

    private async Task ClearDirectAsync(CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null) return;

            _state.Status = GoalStatus.Unmet;
            _state.AchievedAt = _clock.GetUtcNow();
        }
        finally
        {
            _stateLock.Release();
        }

        _engineCts?.Cancel();
        await _heartbeat.ResetAsync().ConfigureAwait(false);
        await RestorePermissionModeAsync(cancellationToken).ConfigureAwait(false);
        _logger?.LogInformation(L.T(StringKey.GoalEngineCleared), _state?.GoalId);
    }

    public async Task MarkCompletedAsync(string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (_lifecyclePipeline is not null)
        {
            await MarkCompletedViaPipelineAsync(reason, cancellationToken).ConfigureAwait(false);
            return;
        }

        await MarkCompletedDirectAsync(reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkCompletedViaPipelineAsync(string reason, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Achieved;
            _state.AchievedAt = _clock.GetUtcNow();
            _state.LastEvaluation = GoalEvaluationResult.Completed(reason);
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.MarkCompleted,
            Reason = reason,
            CancellationToken = cancellationToken,
            State = _state ?? new GoalState { GoalId = string.Empty, Objective = string.Empty, Status = GoalStatus.Achieved },
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
            SavedPermissionMode = _savedPermissionMode,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        _savedPermissionMode = ctx.SavedPermissionMode;

        if (ctx.ShouldCancelEngineLoop)
        {
            _engineCts?.Cancel();
        }

        if (ctx.ShouldResetHeartbeat)
        {
            await _heartbeat.ResetAsync().ConfigureAwait(false);
        }

        if (ctx.ShouldSignalCompletion)
        {
            _completionTcs?.TrySetResult();
        }

        _logger?.LogInformation(L.T(StringKey.GoalEngineCompletedByModel), _state?.GoalId, reason);
    }

    private async Task MarkCompletedDirectAsync(string reason, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Achieved;
            _state.AchievedAt = _clock.GetUtcNow();
            _state.LastEvaluation = GoalEvaluationResult.Completed(reason);
        }
        finally
        {
            _stateLock.Release();
        }

        _engineCts?.Cancel();
        await _heartbeat.ResetAsync().ConfigureAwait(false);
        await RestorePermissionModeAsync(cancellationToken).ConfigureAwait(false);
        _completionTcs?.TrySetResult();
        _logger?.LogInformation(L.T(StringKey.GoalEngineCompletedByModel), _state?.GoalId, reason);
    }

    public async Task MarkUnmetAsync(string reason, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (_lifecyclePipeline is not null)
        {
            await MarkUnmetViaPipelineAsync(reason, cancellationToken).ConfigureAwait(false);
            return;
        }

        await MarkUnmetDirectAsync(reason, cancellationToken).ConfigureAwait(false);
    }

    private async Task MarkUnmetViaPipelineAsync(string reason, CancellationToken cancellationToken)
    {
        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Unmet;
            _state.AchievedAt = _clock.GetUtcNow();
            _state.LastEvaluation = GoalEvaluationResult.NotCompleted(reason);
        }
        finally
        {
            _stateLock.Release();
        }

        var ctx = new GoalLifecycleContext
        {
            Operation = GoalOperation.MarkUnmet,
            Reason = reason,
            CancellationToken = cancellationToken,
            State = _state ?? new GoalState { GoalId = string.Empty, Objective = string.Empty, Status = GoalStatus.Unmet },
            ChatHistory = _chatHistory,
            Heartbeat = _heartbeat,
            PermissionManager = _permissionManager,
            SavedPermissionMode = _savedPermissionMode,
        };

        var pipeline = _lifecyclePipeline;
        if (pipeline is not null)
        {
            await pipeline.ExecuteAsync(ctx, cancellationToken).ConfigureAwait(false);
        }

        _savedPermissionMode = ctx.SavedPermissionMode;

        if (ctx.ShouldCancelEngineLoop)
        {
            _engineCts?.Cancel();
        }

        if (ctx.ShouldResetHeartbeat)
        {
            await _heartbeat.ResetAsync().ConfigureAwait(false);
        }

        if (ctx.ShouldSignalCompletion)
        {
            _completionTcs?.TrySetResult();
        }

        _logger?.LogInformation(L.T(StringKey.GoalEngineUnmetByModel), _state?.GoalId, reason);
    }

    private async Task MarkUnmetDirectAsync(string reason, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await _stateLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_state == null || _state.Status != GoalStatus.Pursuing) return;

            _state.Status = GoalStatus.Unmet;
            _state.AchievedAt = _clock.GetUtcNow();
            _state.LastEvaluation = GoalEvaluationResult.NotCompleted(reason);
        }
        finally
        {
            _stateLock.Release();
        }

        _engineCts?.Cancel();
        await _heartbeat.ResetAsync().ConfigureAwait(false);
        await RestorePermissionModeAsync(cancellationToken).ConfigureAwait(false);
        _completionTcs?.TrySetResult();
        _logger?.LogInformation(L.T(StringKey.GoalEngineUnmetByModel), _state?.GoalId, reason);
    }

    private async Task RunGoalLoopAsync(CancellationToken ct)
    {
        try
        {
            if (_goalGraph is null || _graphEngine is null || _state is null)
            {
                throw new InvalidOperationException("GoalGraph 未构建 — BuildDefaultGraphIfAbsent 应确保 Graph 总是可用");
            }

            _logger?.LogInformation("[GoalEngine] 使用 Graph 引擎执行: {GraphName}", _goalGraph.Name);
            _state = await _graphEngine.ExecuteAsync(_goalGraph, _state, _chatHistory, ct).ConfigureAwait(false);
        }
        finally
        {
            _completionTcs?.TrySetResult();
        }
    }

    private string GenerateGoalId()
    {
        var counter = Interlocked.Increment(ref _goalCounter);
        return $"goal_{counter:D4}_{_clock.GetUtcNow():yyyyMMddHHmmss}";
    }

    private ValueTask OnHeartbeatAsync(CancellationToken cancellationToken)
    {
        _logger?.LogDebug(L.T(StringKey.GoalEngineHeartbeatTriggered),
            _state?.GoalId, _state?.TurnsCompleted);

        CheckStagnationAndAlert();

        return ValueTask.CompletedTask;
    }

    private const int StagnationElapsedThresholdSeconds = 3600;
    private const int StagnationMaxTurnsThreshold = 10;
    private const int StagnationCooldownSeconds = 1800;

    private void CheckStagnationAndAlert()
    {
        if (_state is null || _state.Status != GoalStatus.Pursuing)
            return;

        var elapsedSeconds = (int)_state.Elapsed.TotalSeconds;
        if (elapsedSeconds < StagnationElapsedThresholdSeconds)
            return;

        if (_state.TurnsCompleted >= StagnationMaxTurnsThreshold)
            return;

        if (_state.LastEvaluation is { IsCompleted: true })
            return;

        if (_state.StagnationAlertedAt.HasValue)
        {
            var sinceLastAlert = (_clock.GetUtcNow() - _state.StagnationAlertedAt.Value).TotalSeconds;
            if (sinceLastAlert < StagnationCooldownSeconds)
                return;
        }

        var alertPrompt = ContinuationPromptBuilder.BuildStagnationAlertPrompt(
            _state.Objective,
            elapsedSeconds,
            _state.TurnsCompleted);

        _chatHistory.AddSystemMessage(alertPrompt);
        _state.StagnationAlertedAt = _clock.GetUtcNow();

        _logger?.LogWarning(
            "Stagnation alert injected for goal {GoalId}: elapsed={Elapsed}s, turns={Turns}",
            _state.GoalId, elapsedSeconds, _state.TurnsCompleted);
    }

    public async ValueTask DisposeAsync()
    {
        _engineCts?.Cancel();
        await _heartbeat.ResetAsync().ConfigureAwait(false);

        if (_engineLoop != null)
        {
            try
            {
#pragma warning disable VSTHRD003
                await _engineLoop.ConfigureAwait(false);
#pragma warning restore VSTHRD003
            }
            catch (OperationCanceledException)
            {
            }
        }

        if (_savedPermissionMode.HasValue)
        {
            await RestorePermissionModeAsync(CancellationToken.None).ConfigureAwait(false);
        }

        await _heartbeat.DisposeAsync().ConfigureAwait(false);
        _stateLock.Dispose();
        _engineCts?.Dispose();
        _completionTcs?.TrySetCanceled();
    }

    private async Task SwitchToGoalPermissionModeAsync(CancellationToken cancellationToken)
    {
        if (_permissionManager == null) return;

        try
        {
            var currentMode = await _permissionManager.GetCurrentModeAsync(cancellationToken).ConfigureAwait(false);
            if (currentMode == PermissionMode.BypassPermissions || currentMode == PermissionMode.DontAsk)
            {
                _logger?.LogInformation("[GoalEngine] 当前权限模式为 {Mode}，跳过切换到 Auto", currentMode);
                return;
            }

            _savedPermissionMode = currentMode;
            await _permissionManager.SetPermissionModeAsync(PermissionMode.Auto, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation(L.T(StringKey.PermissionModeSwitched), _savedPermissionMode);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.PermissionModeSwitchFailed));
            _savedPermissionMode = null;
        }
    }

    private async Task RestorePermissionModeAsync(CancellationToken cancellationToken)
    {
        if (_permissionManager == null || !_savedPermissionMode.HasValue) return;

        try
        {
            await _permissionManager.SetPermissionModeAsync(_savedPermissionMode.Value, cancellationToken).ConfigureAwait(false);
            _logger?.LogInformation(L.T(StringKey.PermissionModeRestored), _savedPermissionMode.Value);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, L.T(StringKey.PermissionModeRestoreFailed));
        }
        finally
        {
            _savedPermissionMode = null;
        }
    }

    private void RegisterMainAgent(string goalId, string objective, int? tokenBudget)
    {
        var mainAgents = _agentRegistry.GetMainAgents();
        if (mainAgents.Count > 0)
        {
            _logger?.LogInformation("[GoalEngine] mainAgent 已存在: {AgentId}, 跳过注册", mainAgents[0].Id);
            return;
        }

        if (_serviceProvider is null)
        {
            _logger?.LogWarning("[GoalEngine] IServiceProvider 未注入，无法创建 mainAgent，Goal={GoalId}", goalId);
            return;
        }

        var queryEngine = _serviceProvider.GetService<IQueryEngine>();
        if (queryEngine is null)
        {
            _logger?.LogWarning("[GoalEngine] IQueryEngine 未注入，无法创建 mainAgent，Goal={GoalId}", goalId);
            return;
        }

        var mainAgent = new Core.Agents.Coordinator.Agent(
            task: objective,
            options: new SubAgentOptions { DisplayName = "mainAgent", Role = AgentRole.Coordinator },
            queryEngine: queryEngine,
            logger: _logger,
            clock: _clock,
            name: "mainAgent",
            role: AgentRole.Coordinator,
            goalId: goalId,
            tokenBudget: tokenBudget);

        _logger?.LogInformation("[GoalEngine] mainAgent 创建并注册到 Agent.Registry: {AgentId}, Goal={GoalId}", mainAgent.Id, goalId);
    }
}

