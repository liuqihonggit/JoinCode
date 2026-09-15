namespace Core.Goal;


/// <summary>
/// 事件驱动 Graph 调度器 — Channel 替代 NodeCompletedSignal 轮询，节点完成即投递事件。
/// </summary>
/// <remarks>
/// 改进点（相对 <see cref="PollingGraphScheduler"/>）：
/// <list type="bullet">
/// <item>用 <see cref="Channel{T}"/> 替代 <see cref="SemaphoreSlim"/> 等待 — 可携带完成事件数据</item>
/// <item>各节点独立执行，完成即投递 <c>_completedCh</c>，不 <c>Task.WhenAll</c> 同步等整批</item>
/// <item>消除 1 秒超时兜底 — 纯事件驱动 <c>WaitToReadAsync</c></item>
/// </list>
/// 调度器单 Consumer 从 <c>_completedCh</c> 读事件检查终止；Worker 并发执行节点受 limiter 限流。
/// </remarks>
internal sealed class EventDrivenGraphScheduler : IGraphScheduler
{
    private readonly ILogger? _logger;

    /// <summary>
    /// 构造 EventDrivenGraphScheduler — 注入可选日志记录器
    /// </summary>
    /// <param name="logger">可选日志记录器</param>
    public EventDrivenGraphScheduler(ILogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<NodeCompletionOutcome> RunAsync(
        GoalGraph graph,
        GraphExecutionContext context,
        ProcessNodeCompletionAsync processNodeAsync,
        AsyncLock? concurrencyLimiter,
        CancellationToken ct)
    {
        var completedCh = Channel.CreateUnbounded<NodeCompletionOutcome>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

        var pendingCount = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            while (completedCh.Reader.TryRead(out var completedOutcome))
            {
                pendingCount--;
                if (completedOutcome == NodeCompletionOutcome.GoalAchieved)
                    return completedOutcome;
                if (completedOutcome == NodeCompletionOutcome.GoalUnmet)
                    return completedOutcome;
            }

            var batch = DrainReadyBatch(graph, context);

            if (batch.Count == 0)
            {
                if (context.ReadyQueue.IsEmpty && pendingCount == 0)
                    return NodeCompletionOutcome.Continue;

                if (pendingCount > 0)
                {
                    try
                    {
                        await completedCh.Reader.WaitToReadAsync(ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested)
                    {
                        throw;
                    }
                }
                continue;
            }

            pendingCount += batch.Count;
            foreach (var nodeId in batch)
            {
                _ = ExecuteAndReportAsync(
                    nodeId, graph, context, processNodeAsync, concurrencyLimiter, completedCh, ct);
            }
        }
    }

    /// <summary>
    /// 从就绪队列批量取出所有上游已完成的节点（同层节点，可并行执行）。
    /// 未就绪节点重新入队，待下一轮处理。
    /// </summary>
    private static List<string> DrainReadyBatch(GoalGraph graph, GraphExecutionContext context)
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
    /// 执行单个节点 + 完成后逻辑，并将 outcome 投递到 completedCh。
    /// </summary>
    private async Task ExecuteAndReportAsync(
        string nodeId,
        GoalGraph graph,
        GraphExecutionContext context,
        ProcessNodeCompletionAsync processNodeAsync,
        AsyncLock? limiter,
        Channel<NodeCompletionOutcome> completedCh,
        CancellationToken ct)
    {
        IDisposable? releaser = null;
        if (limiter is not null)
            releaser = await limiter.TryLockAsync(ct).ConfigureAwait(false)
                ?? throw new System.TimeoutException($"锁 '{limiter.Name}' 等待超时");
        using (releaser)
        {
            var dagNode = graph.Dag.Nodes[nodeId];
            var outcome = await processNodeAsync(nodeId, dagNode, context, ct).ConfigureAwait(false);
            context.NodeCompletedSignal.Release();
            await completedCh.Writer.WriteAsync(outcome, ct).ConfigureAwait(false);
        }
    }
}
