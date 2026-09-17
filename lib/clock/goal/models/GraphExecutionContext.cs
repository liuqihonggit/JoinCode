namespace Core.Goal;

/// <summary>
/// 节点执行状态枚举
/// </summary>
public enum NodeStatus
{
    /// <summary>待执行（含重试中）</summary>
    [EnumValue("pending")]
    Pending,
    /// <summary>已完成</summary>
    [EnumValue("completed")]
    Completed,
    /// <summary>失败</summary>
    [EnumValue("failed")]
    Failed,
}

/// <summary>
/// 节点执行状态 — 合并重试计数与完成/失败标记
/// </summary>
public sealed record NodeExecutionState
{
    /// <summary>节点状态</summary>
    public NodeStatus Status { get; init; } = NodeStatus.Pending;
    /// <summary>重试次数</summary>
    public int RetryCount { get; init; }
}


/// <summary>
/// Graph 执行的运行时上下文 — 持有可变状态、队列、重试计数
/// </summary>
/// <remarks>由 <see cref="IGraphScheduler"/> 实现访问，public 以支持自定义调度器。</remarks>
public sealed class GraphExecutionContext
{
    /// <summary>目标图定义</summary>
    public required GoalGraph Graph { get; init; }
    /// <summary>目标状态</summary>
    public required GoalState State { get; init; }
    /// <summary>聊天历史</summary>
    public required MessageList ChatHistory { get; init; }
    /// <summary>状态锁（保护 goalState.Status 写入）</summary>
    public required AsyncLock StateLock { get; init; }
    /// <summary>时钟服务</summary>
    public required IClockService Clock { get; init; }

    /// <summary>就绪节点队列 — 待执行的节点 ID</summary>
    public ConcurrentQueue<string> ReadyQueue { get; } = new();

    /// <summary>
    /// 节点完成信号 — 替代 Task.Delay 轮询。每个节点完成时 Release,循环在 batch 为空时 WaitAsync。
    /// </summary>
    public SemaphoreSlim NodeCompletedSignal { get; } = new(0, int.MaxValue);
    /// <summary>节点执行状态（按节点 ID 索引，合并重试计数与完成/失败标记）</summary>
    public ConcurrentDictionary<string, NodeExecutionState> NodeStates { get; } = new(StringComparer.Ordinal);

    /// <summary>判断节点是否已完成</summary>
    public bool IsNodeCompleted(string nodeId) =>
        NodeStates.TryGetValue(nodeId, out var state) && state.Status == NodeStatus.Completed;

    /// <summary>判断节点是否已失败</summary>
    public bool IsNodeFailed(string nodeId) =>
        NodeStates.TryGetValue(nodeId, out var state) && state.Status == NodeStatus.Failed;

    /// <summary>判断节点是否已结束（完成或失败）</summary>
    public bool IsNodeFinished(string nodeId) =>
        NodeStates.TryGetValue(nodeId, out var state) && state.Status is NodeStatus.Completed or NodeStatus.Failed;

    /// <summary>获取节点重试次数（不存在返回 0）</summary>
    public int GetRetryCount(string nodeId) =>
        NodeStates.TryGetValue(nodeId, out var state) ? state.RetryCount : 0;

    /// <summary>已完成节点数量</summary>
    public int CompletedCount => NodeStates.Count(static kvp => kvp.Value.Status == NodeStatus.Completed);

    /// <summary>失败节点数量</summary>
    public int FailedCount => NodeStates.Count(static kvp => kvp.Value.Status == NodeStatus.Failed);

    /// <summary>标记节点完成（保留已有重试计数）</summary>
    public void MarkNodeCompleted(string nodeId)
    {
        NodeStates.AddOrUpdate(nodeId,
            new NodeExecutionState { Status = NodeStatus.Completed },
            (_, existing) => existing with { Status = NodeStatus.Completed });
    }

    /// <summary>标记节点失败（保留已有重试计数）</summary>
    public void MarkNodeFailed(string nodeId)
    {
        NodeStates.AddOrUpdate(nodeId,
            new NodeExecutionState { Status = NodeStatus.Failed },
            (_, existing) => existing with { Status = NodeStatus.Failed });
    }

    /// <summary>重置节点状态（移除记录，回到初始）</summary>
    public void ResetNodeState(string nodeId)
    {
        NodeStates.TryRemove(nodeId, out _);
    }

    /// <summary>设置节点重试次数（状态置为 Pending）</summary>
    public void SetRetryCount(string nodeId, int count)
    {
        NodeStates[nodeId] = new NodeExecutionState { Status = NodeStatus.Pending, RetryCount = count };
    }

    /// <summary>
    /// 全局循环迭代计数（负向评价-修复循环）
    /// </summary>
    public int GlobalLoopIteration { get; set; }

    /// <summary>
    /// 协调者终止标记（窥探或接管时设置）
    /// </summary>
    public bool CoordinatorTerminated { get; set; }

    /// <summary>
    /// 累计 token 消耗（不受节点重置影响，用于循环终止判定）
    /// </summary>
    public int TotalTokensConsumed { get; set; }

    /// <summary>
    /// T8.3: 团队 ID — /goal 接入 team 组件后，图执行期间创建的团队 ID
    /// null 表示未接入团队（单 Agent 退化模式）
    /// </summary>
    public string? TeamId { get; set; }

    /// <summary>
    /// 判断节点的所有上游（无标签边）是否全部完成或失败。
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    /// <returns>全部上游已完成或失败返回 true；否则 false</returns>
    public bool AreAllUpstreamsCompleted(string nodeId)
    {
        if (!Graph.Dag.Nodes.TryGetValue(nodeId, out var node))
            return false;

        foreach (var edgeId in node.InEdgeIds)
        {
            if (!Graph.Dag.Edges.TryGetValue(edgeId, out var edge))
                continue;

            if (edge.Label.Length > 0)
                continue;

            if (!IsNodeFinished(edge.FromId))
                return false;
        }

        return true;
    }

    /// <summary>
    /// 统计节点已完成或失败的上游数量。
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    /// <returns>已完成或失败的上游数</returns>
    public int CountCompletedUpstreams(string nodeId)
    {
        if (!Graph.Dag.Nodes.TryGetValue(nodeId, out var node))
            return 0;

        var count = 0;
        foreach (var edgeId in node.InEdgeIds)
        {
            if (!Graph.Dag.Edges.TryGetValue(edgeId, out var edge))
                continue;
            if (edge.Label.Length > 0)
                continue;
            if (IsNodeFinished(edge.FromId))
                count++;
        }

        return count;
    }

    /// <summary>
    /// 统计节点成功完成的上游数量。
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    /// <returns>成功完成的上游数</returns>
    public int CountSuccessfulUpstreams(string nodeId)
    {
        if (!Graph.Dag.Nodes.TryGetValue(nodeId, out var node))
            return 0;

        var count = 0;
        foreach (var edgeId in node.InEdgeIds)
        {
            if (!Graph.Dag.Edges.TryGetValue(edgeId, out var edge))
                continue;
            if (edge.Label.Length > 0)
                continue;
            if (IsNodeCompleted(edge.FromId))
                count++;
        }

        return count;
    }

    /// <summary>
    /// 统计节点总上游数量（无标签边）。
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    /// <returns>总上游数</returns>
    public int CountTotalUpstreams(string nodeId)
    {
        if (!Graph.Dag.Nodes.TryGetValue(nodeId, out var node))
            return 0;

        var count = 0;
        foreach (var edgeId in node.InEdgeIds)
        {
            if (!Graph.Dag.Edges.TryGetValue(edgeId, out var edge))
                continue;
            if (edge.Label.Length > 0)
                continue;
            count++;
        }

        return count;
    }

    /// <summary>
    /// 收集节点所有上游的输出（按上游节点 ID 索引）。
    /// </summary>
    /// <param name="nodeId">节点 ID</param>
    /// <returns>上游 ID → 输出 的字典</returns>
    public Dictionary<string, string?> CollectUpstreamOutputs(string nodeId)
    {
        var outputs = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (!Graph.Dag.Nodes.TryGetValue(nodeId, out var node))
            return outputs;

        foreach (var edgeId in node.InEdgeIds)
        {
            if (!Graph.Dag.Edges.TryGetValue(edgeId, out var edge))
                continue;
            if (edge.Label.Length > 0)
                continue;
            if (Graph.Dag.Nodes.TryGetValue(edge.FromId, out var upstream))
            {
                outputs[edge.FromId] = upstream.Payload.Output;
            }
        }

        return outputs;
    }

    /// <summary>
    /// 根据路由匹配模式获取后继节点 ID 列表。
    /// </summary>
    /// <param name="fromNodeId">起始节点 ID</param>
    /// <param name="routes">路由标签数组（null 表示无路由）</param>
    /// <param name="matchMode">路由匹配模式</param>
    /// <returns>后继节点 ID 列表</returns>
    public IReadOnlyList<string> GetNextNodeIds(string fromNodeId, string[]? routes, RouteMatchMode matchMode)
    {
        var nextIds = new List<string>();
        if (!Graph.Dag.Nodes.TryGetValue(fromNodeId, out var node))
            return nextIds;

        var routeSet = routes is not null
            ? new HashSet<string>(routes, StringComparer.Ordinal)
            : [];

        var hasConditionalMatch = false;

        foreach (var edgeId in node.OutEdgeIds)
        {
            if (!Graph.Dag.Edges.TryGetValue(edgeId, out var edge))
                continue;

            if (edge.Label.Length == 0)
            {
                switch (matchMode)
                {
                    case RouteMatchMode.UnconditionalOnly:
                    case RouteMatchMode.All:
                        nextIds.Add(edge.ToId);
                        break;
                }
            }
            else
            {
                if (routeSet.Contains(edge.Label))
                {
                    nextIds.Add(edge.ToId);
                    hasConditionalMatch = true;
                }
            }
        }

        if (!hasConditionalMatch && matchMode == RouteMatchMode.ConditionalOnly)
        {
            foreach (var edgeId in node.OutEdgeIds)
            {
                if (!Graph.Dag.Edges.TryGetValue(edgeId, out var edge))
                    continue;
                if (edge.Label.Length == 0)
                {
                    nextIds.Add(edge.ToId);
                }
            }
        }

        return nextIds;
    }
}
