namespace Core.Goal;

/// <summary>
/// 节点完成后处理上下文 — 责任链各 handler 共享的可变上下文。
/// </summary>
internal sealed class NodePostCompletionContext {
    /// <summary>节点 ID</summary>
    public required string NodeId { get; init; }
    /// <summary>节点负载</summary>
    public required GoalNodePayload Payload { get; init; }
    /// <summary>图执行上下文</summary>
    public required GraphExecutionContext Context { get; init; }
    /// <summary>取消令牌</summary>
    public required CancellationToken Ct { get; init; }
    /// <summary>是否应终止循环（handler 设置后管道短路）</summary>
    public bool ShouldTerminateLoop { get; set; }
}

/// <summary>
/// 节点完成后处理 handler 接口 — 责任链模式。
/// <para>每个 handler 处理一个环节，可设置 ShouldTerminateLoop 短路管道。</para>
/// </summary>
internal interface INodePostCompletionHandler {
    /// <summary>handler 名称（用于日志和诊断）</summary>
    string Name { get; }
    /// <summary>优先级（升序执行）</summary>
    int Priority { get; }
    /// <summary>异步处理节点完成后逻辑</summary>
    Task HandleAsync(NodePostCompletionContext ctx);
}

/// <summary>
/// 元数据提取 handler — 从 neg_review/fix_neg 节点输出中提取 JSON 元数据。
/// <para>优先级 10（最先执行，提取 Routes 供后续 handler 使用）。</para>
/// </summary>
internal sealed class MetadataExtractHandler : INodePostCompletionHandler {
    /// <inheritdoc/>
    public string Name => "MetadataExtract";
    /// <inheritdoc/>
    public int Priority => 10;

    /// <inheritdoc/>
    public Task HandleAsync(NodePostCompletionContext ctx) {
        ExtractNegReviewMetadata(ctx.NodeId, ctx.Payload);
        return Task.CompletedTask;
    }

    /// <summary>从 neg_review / fix_neg 节点输出中提取 JSON 元数据并写入 payload</summary>
    private static void ExtractNegReviewMetadata(string nodeId, GoalNodePayload payload) {
        if (string.IsNullOrEmpty(payload.Output))
            return;

        if (nodeId.Equals("neg_review", StringComparison.Ordinal)) {
            var negReview = LlmJsonHelper.Deserialize(payload.Output, GoalJsonContext.Default.NegReviewOutputJson, out var negRepair);
            if (negReview is null)
                return;

            payload.NegativeReviewCount = negReview.NegativeReviewCount;
            payload.NegativeReviewTaskId = negReview.TaskId;
            if (!string.IsNullOrEmpty(negReview.Route)) {
                payload.Routes = [negReview.Route];
            }
        } else if (nodeId.Equals("fix_neg", StringComparison.Ordinal)) {
            var fixNeg = LlmJsonHelper.Deserialize(payload.Output, GoalJsonContext.Default.FixNegOutputJson, out _);
            if (fixNeg is not null && !string.IsNullOrEmpty(fixNeg.Route)) {
                payload.Routes = [fixNeg.Route];
            }
        }
    }
}

/// <summary>
/// 用户交互 handler — 负向评价循环中询问用户是否继续。
/// <para>优先级 20（元数据提取后，循环观察前）。</para>
/// </summary>
internal sealed class UserInteractionHandler : INodePostCompletionHandler {
    private readonly IGoalUserInteraction? _userInteraction;
    private readonly ILogger? _logger;

    /// <summary>初始化用户交互 handler</summary>
    public UserInteractionHandler(IGoalUserInteraction? userInteraction, ILogger? logger) {
        _userInteraction = userInteraction;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "UserInteraction";
    /// <inheritdoc/>
    public int Priority => 20;

    /// <inheritdoc/>
    public async Task HandleAsync(NodePostCompletionContext ctx) {
        if (_userInteraction is null)
            return;

        var payload = ctx.Payload;
        if (payload.NegativeReviewCount < 6 || payload.NegativeReviewCount > 10)
            return;

        var decision = await _userInteraction.AskToContinueAsync(
            $"负向评价发现 {payload.NegativeReviewCount} 条不足，是否继续循环修复？",
            payload.NegativeReviewCount,
            ctx.Context.GlobalLoopIteration,
            timeoutSeconds: 60,
            cancellationToken: ctx.Ct).ConfigureAwait(false);

        if (decision.CoordinatorTakenOver) {
            ctx.Context.CoordinatorTerminated = true;
            _logger?.LogWarning("[GoalGraph] 协调者接管: {Reason} (节点={NodeId}, 负评={NegCount})",
                decision.Reason, ctx.NodeId, payload.NegativeReviewCount);
            return;
        }

        if (!decision.ShouldContinue) {
            payload.Routes = new[] { "NEG_STOP" };
            _logger?.LogInformation("[GoalGraph] 用户选择停止循环 (节点={NodeId}, 负评={NegCount})",
                ctx.NodeId, payload.NegativeReviewCount);
        }
    }
}

/// <summary>
/// 循环观察 handler — 协调者窥探循环状态，决定是否终止。
/// <para>优先级 30（用户交互后，终止判断前）。</para>
/// </summary>
internal sealed class LoopObservationHandler : INodePostCompletionHandler {
    private readonly IGoalNodeInspector? _nodeInspector;
    private readonly ILogger? _logger;

    /// <summary>初始化循环观察 handler</summary>
    public LoopObservationHandler(IGoalNodeInspector? nodeInspector, ILogger? logger) {
        _nodeInspector = nodeInspector;
        _logger = logger;
    }

    /// <inheritdoc/>
    public string Name => "LoopObservation";
    /// <inheritdoc/>
    public int Priority => 30;

    /// <inheritdoc/>
    public async Task HandleAsync(NodePostCompletionContext ctx) {
        if (_nodeInspector is null)
            return;

        var payload = ctx.Payload;
        if (!ctx.NodeId.Equals("neg_review", StringComparison.Ordinal) && !ctx.NodeId.Equals("fix_neg", StringComparison.Ordinal))
            return;

        var observationContext = new LoopObservationContext {
            GoalId = ctx.Context.State.GoalId,
            NodeId = ctx.NodeId,
            LoopIteration = ctx.Context.GlobalLoopIteration,
            NegativeReviewCount = payload.NegativeReviewCount,
            TotalTokensConsumed = ctx.Context.TotalTokensConsumed,
            TotalTurnsCompleted = ctx.Context.State.TurnsCompleted,
            LastNodeOutput = payload.Output,
            NegativeReviewTaskId = payload.NegativeReviewTaskId,
        };

        var shouldTerminate = await _nodeInspector.ObserveLoopAsync(observationContext, ctx.Ct).ConfigureAwait(false);

        if (shouldTerminate) {
            ctx.Context.CoordinatorTerminated = true;
            _logger?.LogInformation("[GoalGraph] 协调者窥探终止: 节点={NodeId}, 迭代={Iter}, 负评={NegCount}",
                ctx.NodeId, ctx.Context.GlobalLoopIteration, payload.NegativeReviewCount);
        }
    }
}

/// <summary>
/// 循环终止判断 handler — 检查是否满足终止条件。
/// <para>优先级 40（最后执行，综合前面 handler 设置的状态）。</para>
/// </summary>
internal sealed class TerminationCheckHandler : INodePostCompletionHandler {
    /// <inheritdoc/>
    public string Name => "TerminationCheck";
    /// <inheritdoc/>
    public int Priority => 40;

    /// <inheritdoc/>
    public Task HandleAsync(NodePostCompletionContext ctx) {
        if (CheckTermination(ctx.Payload, ctx.Context)) {
            ctx.ShouldTerminateLoop = true;
        }
        return Task.CompletedTask;
    }

    /// <summary>判断是否应终止负向评价-修复循环（纵深防御，任一满足即终止）</summary>
    private static bool CheckTermination(GoalNodePayload payload, GraphExecutionContext context) {
        if (context.CoordinatorTerminated)
            return true;

        if (context.GlobalLoopIteration >= context.Graph.HardMaxLoopIterations)
            return true;

        if (context.State.TokenBudget.HasValue && context.TotalTokensConsumed >= context.State.TokenBudget.Value)
            return true;

        if (context.State.TurnBudget.HasValue && context.GlobalLoopIteration >= context.State.TurnBudget.Value)
            return true;

        return false;
    }
}

/// <summary>
/// 节点完成后处理管道 — 责任链模式，按 Priority 升序执行所有 handler。
/// <para>从 GoalGraphEngine 提取，消除 Engine 对循环控制逻辑的直接依赖。</para>
/// <para>短路：任一 handler 设置 ShouldTerminateLoop 后停止后续 handler。</para>
/// </summary>
internal sealed class NodeCompletionPipeline {
    private readonly INodePostCompletionHandler[] _handlers;
    private readonly ILogger? _logger;

    /// <summary>初始化节点完成后处理管道</summary>
    /// <param name="handlers">handler 列表</param>
    /// <param name="logger">可选日志记录器</param>
    public NodeCompletionPipeline(IEnumerable<INodePostCompletionHandler> handlers, ILogger? logger) {
        _handlers = handlers.OrderBy(h => h.Priority).ToArray();
        _logger = logger;
    }

    /// <summary>按优先级顺序执行所有 handler，ShouldTerminateLoop 时短路</summary>
    /// <param name="ctx">节点完成后处理上下文</param>
    public async Task ExecuteAsync(NodePostCompletionContext ctx) {
        foreach (var handler in _handlers) {
            try {
                await handler.HandleAsync(ctx).ConfigureAwait(false);
            } catch (Exception ex) when (ex is not OperationCanceledException) {
                _logger?.LogError(ex, "[GoalGraph] 责任链 handler {Name} 异常", handler.Name);
            }

            if (ctx.ShouldTerminateLoop)
                break;
        }
    }
}