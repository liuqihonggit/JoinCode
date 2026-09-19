namespace Core.Goal;


/// <summary>
/// 轮询式 Graph 调度器 — while(true) + DrainReadyBatch + NodeCompletedSignal.WaitAsync。
/// </summary>
/// <remarks>
/// 提取自 GoalGraphEngine.ExecuteAsync 旧实现，保持行为等价。
/// 当 batch 为空时通过 <see cref="SemaphoreSlim.WaitAsync(TimeSpan, CancellationToken)"/> 等待节点完成信号（替代 Task.Delay 轮询）。
/// </remarks>
internal sealed class PollingGraphScheduler : IGraphScheduler {
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造 PollingGraphScheduler — 注入可选日志记录器
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public PollingGraphScheduler(ILogger? logger = null) {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<NodeCompletionOutcome> RunAsync(
        GoalGraph graph,
        GraphExecutionContext context,
        ProcessNodeCompletionAsync processNodeAsync,
        AsyncLock? concurrencyLimiter,
        CancellationToken ct) {
        while (true) {
            ct.ThrowIfCancellationRequested();

            var batch = DrainReadyBatch(graph, context);

            if (batch.Count == 0) {
                if (context.ReadyQueue.IsEmpty)
                    return NodeCompletionOutcome.Continue;
                try {
                    await context.NodeCompletedSignal.WaitAsync(TimeSpan.FromSeconds(1), ct).ConfigureAwait(false);
                } catch (TimeoutException) { _logger?.LogDebug("[GoalGraph] 节点完成信号等待超时,继续检查"); }
                continue;
            }

            var outcomes = await Task.WhenAll(batch.Select(nodeId =>
                ExecuteWithSemaphoreAsync(nodeId, graph, context, processNodeAsync, concurrencyLimiter, ct))).ConfigureAwait(false);

            foreach (var outcome in outcomes) {
                if (outcome == NodeCompletionOutcome.GoalAchieved)
                    return outcome;
                if (outcome == NodeCompletionOutcome.GoalUnmet)
                    return outcome;
            }
        }
    }

    /// <summary>
    /// 从就绪队列批量取出所有上游已完成的节点（同层节点，可并行执行）。
    /// 未就绪节点重新入队，待下一轮处理。
    /// </summary>
    private static List<string> DrainReadyBatch(GoalGraph graph, GraphExecutionContext context) {
        var batch = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var deferred = new List<string>();

        while (context.ReadyQueue.TryDequeue(out var nodeId)) {
            if (!seen.Add(nodeId))
                continue;

            if (context.IsNodeCompleted(nodeId))
                continue;

            if (!context.AreAllUpstreamsCompleted(nodeId)) {
                deferred.Add(nodeId);
                continue;
            }

            if (!graph.Dag.Nodes.TryGetValue(nodeId, out var dagNode))
                continue;

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
    private static async Task<NodeCompletionOutcome> ExecuteWithSemaphoreAsync(
        string nodeId,
        GoalGraph graph,
        GraphExecutionContext context,
        ProcessNodeCompletionAsync processNodeAsync,
        AsyncLock? limiter,
        CancellationToken ct) {
        IDisposable? releaser = null;
        if (limiter is not null)
            releaser = await limiter.TryLockAsync(ct).ConfigureAwait(false)
                ?? throw new System.TimeoutException($"锁 '{limiter.Name}' 等待超时");
        using (releaser) {
            var dagNode = graph.Dag.Nodes[nodeId];
            var outcome = await processNodeAsync(nodeId, dagNode, context, ct).ConfigureAwait(false);
            context.NodeCompletedSignal.Release();
            return outcome;
        }
    }
}