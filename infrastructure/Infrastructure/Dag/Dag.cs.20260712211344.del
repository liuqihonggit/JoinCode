namespace Infrastructure.Dag;

/// <summary>
/// 通用有向无环图 — 拓扑排序、环检测、增量重算
/// </summary>
public sealed class Dag<T>
{
    private readonly Dictionary<string, DagNode<T>> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DagEdge> _edges = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _adjacency = new(StringComparer.Ordinal);
    private readonly Dictionary<string, HashSet<string>> _reverseAdjacency = new(StringComparer.Ordinal);
    private int _version;

    public IReadOnlyDictionary<string, DagNode<T>> Nodes => _nodes;
    public IReadOnlyDictionary<string, DagEdge> Edges => _edges;
    public int Version => _version;

    /// <summary>
    /// 添加节点
    /// </summary>
    public DagResult AddNode(DagNode<T> node)
    {
        if (_nodes.ContainsKey(node.Id))
            return DagResult.Fail($"Node already exists: {node.Id}");

        _nodes[node.Id] = node;
        if (!_adjacency.ContainsKey(node.Id))
            _adjacency[node.Id] = new HashSet<string>(StringComparer.Ordinal);
        if (!_reverseAdjacency.ContainsKey(node.Id))
            _reverseAdjacency[node.Id] = new HashSet<string>(StringComparer.Ordinal);
        _version++;
        return DagResult.Ok();
    }

    /// <summary>
    /// 添加边 — 自动检测环
    /// </summary>
    public DagResult AddEdge(DagEdge edge)
    {
        if (!_nodes.ContainsKey(edge.FromId))
            return DagResult.Fail($"Source node not found: {edge.FromId}");
        if (!_nodes.ContainsKey(edge.ToId))
            return DagResult.Fail($"Target node not found: {edge.ToId}");

        if (WouldCreateCycle(edge.FromId, edge.ToId))
            return DagResult.Cycle(FindCyclePath(edge.FromId, edge.ToId));

        AddEdgeInternal(edge);
        return DagResult.Ok();
    }

    /// <summary>
    /// 添加边 — 允许产生环（用于环检测场景，如 DI 审计）
    /// </summary>
    public DagResult TryAddEdge(DagEdge edge)
    {
        if (!_nodes.ContainsKey(edge.FromId))
            return DagResult.Fail($"Source node not found: {edge.FromId}");
        if (!_nodes.ContainsKey(edge.ToId))
            return DagResult.Fail($"Target node not found: {edge.ToId}");

        AddEdgeInternal(edge);
        return DagResult.Ok();
    }

    /// <summary>
    /// 判断添加 from→to 边是否会产生环
    /// </summary>
    public bool WouldCreateCycle(string fromId, string toId)
    {
        if (fromId == toId) return true;
        return GetDescendants(toId).Any(d => d.Id == fromId);
    }

    /// <summary>
    /// 静态工具：判断在给定邻接表中添加 from→to 是否会产生环（无需构建 Dag 实例）
    /// </summary>
    public static bool WouldCreateCycle(IReadOnlyDictionary<string, IReadOnlyList<string>> adjacency, string fromId, string toId)
    {
        if (fromId == toId) return true;

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(toId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == fromId) return true;
            if (!visited.Add(current)) continue;

            if (adjacency.TryGetValue(current, out var targets))
            {
                foreach (var target in targets)
                    queue.Enqueue(target);
            }
        }

        return false;
    }

    /// <summary>
    /// 移除节点 — 同时移除关联边
    /// </summary>
    public DagResult RemoveNode(string nodeId)
    {
        if (!_nodes.TryGetValue(nodeId, out var node))
            return DagResult.Fail($"Node not found: {nodeId}");

        var edgeIdsToRemove = node.InEdgeIds.Concat(node.OutEdgeIds).ToList();
        foreach (var edgeId in edgeIdsToRemove)
        {
            RemoveEdgeInternal(edgeId);
        }

        _adjacency.Remove(nodeId);
        foreach (var targets in _adjacency.Values)
            targets.Remove(nodeId);

        _reverseAdjacency.Remove(nodeId);
        foreach (var sources in _reverseAdjacency.Values)
            sources.Remove(nodeId);

        _nodes.Remove(nodeId);
        _version++;
        return DagResult.Ok();
    }

    /// <summary>
    /// 移除边 — 保留节点
    /// </summary>
    public DagResult RemoveEdge(string edgeId)
    {
        if (!_edges.ContainsKey(edgeId))
            return DagResult.Fail($"Edge not found: {edgeId}");

        RemoveEdgeInternal(edgeId);
        _version++;
        return DagResult.Ok();
    }

    /// <summary>
    /// 拓扑排序 — Kahn 算法
    /// </summary>
    public IReadOnlyList<DagNode<T>> TopologicalSort()
    {
        var levels = TopologicalSortByLevels();
        return levels.SelectMany(level => level).ToList();
    }

    /// <summary>
    /// 拓扑排序（分层）— 返回按层级分组的节点列表，同层可并行执行
    /// </summary>
    public IReadOnlyList<IReadOnlyList<DagNode<T>>> TopologicalSortByLevels()
    {
        var inDegree = _nodes.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var edge in _edges.Values)
        {
            inDegree[edge.ToId]++;
        }

        var currentLevel = new List<string>();
        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0)
                currentLevel.Add(kvp.Key);
        }

        var result = new List<IReadOnlyList<DagNode<T>>>();
        while (currentLevel.Count > 0)
        {
            var levelNodes = currentLevel.Select(id => _nodes[id]).ToList();
            result.Add(levelNodes);

            var nextLevel = new List<string>();
            foreach (var id in currentLevel)
            {
                if (!_adjacency.TryGetValue(id, out var targets)) continue;
                foreach (var targetId in targets)
                {
                    inDegree[targetId]--;
                    if (inDegree[targetId] == 0)
                        nextLevel.Add(targetId);
                }
            }

            currentLevel = nextLevel;
        }

        return result;
    }

    /// <summary>
    /// 检测是否存在环
    /// </summary>
    public bool HasCycle()
    {
        var sorted = TopologicalSort();
        return sorted.Count < _nodes.Count;
    }

    /// <summary>
    /// 查找所有环路径
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> FindAllCycles()
    {
        var cycles = new List<IReadOnlyList<string>>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();

        foreach (var nodeId in _nodes.Keys)
        {
            DfsFindCycles(nodeId, visited, stack, path, cycles);
        }

        return cycles;
    }

    /// <summary>
    /// 获取节点的所有上游节点（依赖）
    /// </summary>
    public IReadOnlyList<DagNode<T>> GetAncestors(string nodeId)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        if (_reverseAdjacency.TryGetValue(nodeId, out var parents))
        {
            foreach (var p in parents)
            {
                queue.Enqueue(p);
            }
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!result.Add(id)) continue;

            if (_reverseAdjacency.TryGetValue(id, out var grandParents))
            {
                foreach (var gp in grandParents)
                    queue.Enqueue(gp);
            }
        }

        return result.Select(id => _nodes[id]).ToList();
    }

    /// <summary>
    /// 获取节点的所有下游节点（受影响者）
    /// </summary>
    public IReadOnlyList<DagNode<T>> GetDescendants(string nodeId)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();

        if (_adjacency.TryGetValue(nodeId, out var children))
        {
            foreach (var c in children)
                queue.Enqueue(c);
        }

        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!result.Add(id)) continue;

            if (_adjacency.TryGetValue(id, out var grandChildren))
            {
                foreach (var gc in grandChildren)
                    queue.Enqueue(gc);
            }
        }

        return result.Select(id => _nodes[id]).ToList();
    }

    /// <summary>
    /// 增量重算 — 从指定节点开始，沿拓扑序重算受影响的子图
    /// </summary>
    public IReadOnlyList<DagNode<T>> GetAffectedSubgraph(string changedNodeId)
    {
        var descendants = GetDescendants(changedNodeId);
        var descendantIds = descendants.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        descendantIds.Add(changedNodeId);

        var subgraphNodes = descendantIds
            .Select(id => _nodes[id])
            .ToList();

        var inDegree = subgraphNodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        foreach (var node in subgraphNodes)
        {
            foreach (var edgeId in node.InEdgeIds)
            {
                if (_edges.TryGetValue(edgeId, out var edge) && descendantIds.Contains(edge.FromId))
                    inDegree[node.Id]++;
            }
        }

        var queue = new Queue<string>();
        foreach (var kvp in inDegree)
        {
            if (kvp.Value == 0)
                queue.Enqueue(kvp.Key);
        }

        var result = new List<DagNode<T>>();
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            result.Add(_nodes[id]);

            if (!_adjacency.TryGetValue(id, out var targets)) continue;
            foreach (var targetId in targets)
            {
                if (!descendantIds.Contains(targetId)) continue;
                inDegree[targetId]--;
                if (inDegree[targetId] == 0)
                    queue.Enqueue(targetId);
            }
        }

        return result;
    }

    /// <summary>
    /// 查找 from→to 产生的环路径
    /// </summary>
    private IReadOnlyList<string> FindCyclePath(string fromId, string toId)
    {
        var path = new List<string> { fromId };
        var visited = new HashSet<string>(StringComparer.Ordinal) { fromId };

        if (DfsFindPath(toId, fromId, visited, path))
            return path;

        return [fromId, toId];
    }

    private bool DfsFindPath(string current, string target, HashSet<string> visited, List<string> path)
    {
        if (current == target) return true;

        if (!_adjacency.TryGetValue(current, out var neighbors)) return false;

        foreach (var next in neighbors)
        {
            if (!visited.Add(next)) continue;
            path.Add(next);
            if (DfsFindPath(next, target, visited, path))
                return true;
            path.RemoveAt(path.Count - 1);
        }

        return false;
    }

    private void DfsFindCycles(string nodeId, HashSet<string> visited, HashSet<string> stack, List<string> path, List<IReadOnlyList<string>> cycles)
    {
        if (visited.Contains(nodeId)) return;
        if (stack.Contains(nodeId))
        {
            var cycleStart = path.IndexOf(nodeId);
            if (cycleStart >= 0)
            {
                var cycle = path.Skip(cycleStart).ToList();
                cycles.Add(cycle);
            }
            return;
        }

        visited.Add(nodeId);
        stack.Add(nodeId);
        path.Add(nodeId);

        if (_adjacency.TryGetValue(nodeId, out var neighbors))
        {
            foreach (var next in neighbors)
                DfsFindCycles(next, visited, stack, path, cycles);
        }

        stack.Remove(nodeId);
        path.RemoveAt(path.Count - 1);
    }

    private void RemoveEdgeInternal(string edgeId)
    {
        if (!_edges.TryGetValue(edgeId, out var edge)) return;

        _nodes[edge.FromId].OutEdgeIds.Remove(edgeId);
        _nodes[edge.ToId].InEdgeIds.Remove(edgeId);
        _adjacency[edge.FromId].Remove(edge.ToId);
        _reverseAdjacency[edge.ToId].Remove(edge.FromId);
        _edges.Remove(edgeId);
    }

    private void AddEdgeInternal(DagEdge edge)
    {
        _edges[edge.Id] = edge;
        _nodes[edge.FromId].OutEdgeIds.Add(edge.Id);
        _nodes[edge.ToId].InEdgeIds.Add(edge.Id);
        _adjacency[edge.FromId].Add(edge.ToId);
        _reverseAdjacency[edge.ToId].Add(edge.FromId);
        _version++;
    }
}
