namespace Core.Goal;

using JoinCode.Abstractions.Models.Goal;
using Structura.Dag;

/// <summary>
/// Graph 执行的运行时上下文 — 持有可变状态、队列、重试计数
/// </summary>
internal sealed class GraphExecutionContext
{
    public required GoalGraph Graph { get; init; }
    public required GoalState State { get; init; }
    public required MessageList ChatHistory { get; init; }
    public required SemaphoreSlim StateLock { get; init; }
    public required IClockService Clock { get; init; }

    public Queue<string> ReadyQueue { get; } = new();
    public Dictionary<string, int> RetryCount { get; } = new(StringComparer.Ordinal);
    public HashSet<string> CompletedNodes { get; } = new(StringComparer.Ordinal);
    public HashSet<string> FailedNodes { get; } = new(StringComparer.Ordinal);

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

            if (!CompletedNodes.Contains(edge.FromId) && !FailedNodes.Contains(edge.FromId))
                return false;
        }

        return true;
    }

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
            if (CompletedNodes.Contains(edge.FromId) || FailedNodes.Contains(edge.FromId))
                count++;
        }

        return count;
    }

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
            if (CompletedNodes.Contains(edge.FromId))
                count++;
        }

        return count;
    }

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
