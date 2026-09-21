namespace Core.Goal;

/// <summary>
/// 重试处理器 — 独立类，封装失败回退/重激活逻辑和失败率终止检查。
/// <para>从 GoalGraphEngine 提取，消除 Engine 对重试逻辑的直接依赖。</para>
/// <para>职责单一：检查失败率终止 + 分级重试决策 + 回退重激活子图。</para>
/// </summary>
internal sealed class RetryHandler {
    private readonly IGoalNodeInspector? _nodeInspector;
    private readonly ILogger? _logger;

    /// <summary>初始化重试处理器</summary>
    /// <param name="nodeInspector">可选节点检查器，用于质量评分</param>
    /// <param name="logger">可选日志记录器</param>
    public RetryHandler(IGoalNodeInspector? nodeInspector, ILogger? logger) {
        _nodeInspector = nodeInspector;
        _logger = logger;
    }

    /// <summary>
    /// P1-4: 失败率终止检查 — 至少3个节点完成且失败率>50% 时返回 GoalUnmet。
    /// 阈值3避免小图误触发（2节点1失败=50%不触发，3节点2失败=66%触发）。
    /// </summary>
    /// <param name="context">图执行上下文</param>
    /// <returns>终止决策（GoalUnmet）或 null（继续）</returns>
    public NodeCompletionOutcome? CheckFailureRateTermination(GraphExecutionContext context) {
        var totalFinished = context.CompletedCount + context.FailedCount;
        if (totalFinished >= 3 && (double)context.FailedCount / totalFinished > 0.5) {
            _logger?.LogInformation("[GoalGraph] 失败率过高终止: {Failed}/{Total}",
                context.FailedCount, totalFinished);
            return NodeCompletionOutcome.GoalUnmet;
        }
        return null;
    }

    /// <summary>
    /// 分级重试决策 + 回退重激活子图。
    /// 流程：质量评分 → RetryPolicy.Decide → Accept/Abandon/RetryWithPatch → 回退重激活。
    /// </summary>
    /// <param name="targetNodeId">目标节点 ID</param>
    /// <param name="context">图执行上下文</param>
    /// <param name="ct">取消令牌</param>
    public async Task HandleRetryAsync(string targetNodeId, GraphExecutionContext context, CancellationToken ct) {
        var retryCount = context.GetRetryCount(targetNodeId);

        if (_nodeInspector is not null && context.Graph.Dag.Nodes.TryGetValue(targetNodeId, out var targetNode)) {
            var output = targetNode.Payload.Output ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(output)) {
                var score = await _nodeInspector.ScoreAsync(output, cancellationToken: ct).ConfigureAwait(false);
                var decision = GoalRetryPolicy.Decide(score.Overall, retryCount);

                _logger?.LogInformation("[GoalGraph] 分级重试决策: {NodeId} (分数={Score:F2}, 重试={Retries}, 决策={Decision})",
                    targetNodeId, score.Overall, retryCount, decision);

                switch (decision) {
                    case RetryDecision.Accept:
                    return;
                    case RetryDecision.Abandon:
                    targetNode.Payload.Status = GoalNodeStatus.Failed;
                    targetNode.Payload.ErrorMessage = $"质量分数过低放弃重试 (score={score.Overall:F2}, retries={retryCount})";
                    return;
                    case RetryDecision.RetryWithPatch:
                    break;
                }
            }
        }

        if (retryCount >= context.Graph.MaxRetriesPerNode) {
            _logger?.LogWarning("[GoalGraph] 回退超过最大重试次数: {NodeId} ({Retries}/{Max})",
                targetNodeId, retryCount, context.Graph.MaxRetriesPerNode);

            if (context.Graph.Dag.Nodes.TryGetValue(targetNodeId, out var node)) {
                node.Payload.Status = GoalNodeStatus.Failed;
                node.Payload.ErrorMessage = $"Max retries ({context.Graph.MaxRetriesPerNode}) exceeded";
            }
            return;
        }

        var affected = context.Graph.Dag.GetAffectedSubgraph(targetNodeId);
        foreach (var node in affected) {
            node.Payload.Status = GoalNodeStatus.Pending;
            node.Payload.Output = null;
            node.Payload.Routes = null;
            node.Payload.ErrorMessage = null;
            node.Payload.StartedAt = null;
            node.Payload.CompletedAt = null;
            node.Payload.TokensUsed = 0;
            node.Version++;
            context.ResetNodeState(node.Id);
        }

        context.SetRetryCount(targetNodeId, retryCount + 1);
        context.GlobalLoopIteration++;
        context.ReadyQueue.Enqueue(targetNodeId);

        _logger?.LogInformation("[GoalGraph] 回退重激活: {NodeId} (第{Retry}次, 影响{Count}个节点, 全局迭代={GlobalIter})",
            targetNodeId, retryCount + 1, affected.Count(), context.GlobalLoopIteration);
    }
}