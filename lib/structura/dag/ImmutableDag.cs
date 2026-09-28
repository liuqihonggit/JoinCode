namespace Structura.Dag;

/// <summary>
/// 不可变无锁 DAG — 内部用 ImmutableHamT + ImmutableHamTSet 存储,CAS 循环写,Volatile.Read 读。API 链式风格,失败抛 InvalidOperationException。
/// </summary>
public sealed class ImmutableDag<T> {
    private sealed record DagState(
        ImmutableHamT<string, ImmutableDagNode<T>> Nodes,
        ImmutableHamT<string, DagEdge> Edges,
        ImmutableHamT<(string From, string To), string> EdgesByEndpoints,
        ImmutableHamT<string, ImmutableHamTSet<string>> Adjacency,
        ImmutableHamT<string, ImmutableHamTSet<string>> ReverseAdjacency,
        int Version
    );

    private static readonly ImmutableHamT<string, ImmutableDagNode<T>> EmptyNodes =
        ImmutableHamT.Create<string, ImmutableDagNode<T>>(StringComparer.Ordinal);
    private static readonly ImmutableHamT<string, DagEdge> EmptyEdges =
        ImmutableHamT.Create<string, DagEdge>(StringComparer.Ordinal);
    private static readonly ImmutableHamT<(string, string), string> EmptyEdgeIndex =
        ImmutableHamT.Create<(string, string), string>();
    private static readonly ImmutableHamT<string, ImmutableHamTSet<string>> EmptyAdj =
        ImmutableHamT.Create<string, ImmutableHamTSet<string>>(StringComparer.Ordinal);

    private static readonly DagState InitialState = new(EmptyNodes, EmptyEdges, EmptyEdgeIndex, EmptyAdj, EmptyAdj, 0);

    private DagState _state = InitialState;

    /// <summary>创建空实例</summary>
    public static ImmutableDag<T> Empty => new();

    /// <summary>所有节点的只读快照(无锁)</summary>
    public IReadOnlyDictionary<string, ImmutableDagNode<T>> Nodes => Volatile.Read(ref _state).Nodes;
    /// <summary>所有边的只读快照(无锁)</summary>
    public IReadOnlyDictionary<string, DagEdge> Edges => Volatile.Read(ref _state).Edges;
    /// <summary>图版本号(无锁读取)</summary>
    public int Version => Volatile.Read(ref _state).Version;

    /// <summary>按端点 O(1) 查找边(无锁快照)</summary>
    public bool TryGetEdge(string fromId, string toId, [MaybeNullWhen(false)] out DagEdge edge) {
        var state = Volatile.Read(ref _state);
        if (state.EdgesByEndpoints.TryGetValue((fromId, toId), out var edgeId))
            return state.Edges.TryGetValue(edgeId, out edge);
        edge = null;
        return false;
    }

    /// <summary>添加节点(链式,CAS 无锁)。节点已存在抛 InvalidOperationException</summary>
    public ImmutableDag<T> AddNode(ImmutableDagNode<T> node) {
        while (true) {
            var current = Volatile.Read(ref _state);
            if (current.Nodes.ContainsKey(node.Id))
                throw new InvalidOperationException($"Node already exists: {node.Id}");
            var newState = current with {
                Nodes = current.Nodes.SetItem(node.Id, node),
                Adjacency = current.Adjacency.SetItem(node.Id, ImmutableHamTSet<string>.Empty),
                ReverseAdjacency = current.ReverseAdjacency.SetItem(node.Id, ImmutableHamTSet<string>.Empty),
                Version = current.Version + 1
            };
            if (Interlocked.CompareExchange(ref _state, newState, current) == current)
                return this;
        }
    }

    /// <summary>添加边,自动检测环(链式,CAS 无锁)。环检测失败抛 InvalidOperationException</summary>
    public ImmutableDag<T> AddEdge(DagEdge edge) {
        while (true) {
            var current = Volatile.Read(ref _state);
            if (!current.Nodes.ContainsKey(edge.FromId))
                throw new InvalidOperationException($"Source node not found: {edge.FromId}");
            if (!current.Nodes.ContainsKey(edge.ToId))
                throw new InvalidOperationException($"Target node not found: {edge.ToId}");
            if (CanReach(current, edge.ToId, edge.FromId))
                throw new InvalidOperationException("Cycle detected");
            var newState = AddEdgeToState(current, edge);
            if (Interlocked.CompareExchange(ref _state, newState, current) == current)
                return this;
        }
    }

    /// <summary>添加边,允许产生环(链式,CAS 无锁)</summary>
    public ImmutableDag<T> TryAddEdge(DagEdge edge) {
        while (true) {
            var current = Volatile.Read(ref _state);
            if (!current.Nodes.ContainsKey(edge.FromId))
                throw new InvalidOperationException($"Source node not found: {edge.FromId}");
            if (!current.Nodes.ContainsKey(edge.ToId))
                throw new InvalidOperationException($"Target node not found: {edge.ToId}");
            var newState = AddEdgeToState(current, edge);
            if (Interlocked.CompareExchange(ref _state, newState, current) == current)
                return this;
        }
    }

    /// <summary>移除节点及其关联边(链式,CAS 无锁)。不存在抛 InvalidOperationException</summary>
    public ImmutableDag<T> RemoveNode(string nodeId) {
        while (true) {
            var current = Volatile.Read(ref _state);
            if (!current.Nodes.TryGetValue(nodeId, out var node))
                throw new InvalidOperationException($"Node not found: {nodeId}");
            var newState = RemoveNodeFromState(current, nodeId, node);
            if (Interlocked.CompareExchange(ref _state, newState, current) == current)
                return this;
        }
    }

    /// <summary>移除边,保留节点(链式,CAS 无锁)。不存在抛 InvalidOperationException</summary>
    public ImmutableDag<T> RemoveEdge(string edgeId) {
        while (true) {
            var current = Volatile.Read(ref _state);
            if (!current.Edges.TryGetValue(edgeId, out var edge))
                throw new InvalidOperationException($"Edge not found: {edgeId}");
            var newState = RemoveEdgeFromState(current, edgeId, edge);
            if (Interlocked.CompareExchange(ref _state, newState, current) == current)
                return this;
        }
    }

    /// <summary>判断添加 from→to 边是否会产生环(无锁快照)</summary>
    public bool WouldCreateCycle(string fromId, string toId) {
        if (fromId == toId) return true;
        var state = Volatile.Read(ref _state);
        return CanReach(state, toId, fromId);
    }

    /// <summary>静态工具:判断在给定邻接表中添加 from→to 是否会产生环</summary>
    public static bool WouldCreateCycle(IReadOnlyDictionary<string, IReadOnlyList<string>> adjacency, string fromId, string toId) {
        if (fromId == toId) return true;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(toId);
        while (queue.Count > 0) {
            var current = queue.Dequeue();
            if (current == fromId) return true;
            if (!visited.Add(current)) continue;
            if (adjacency.TryGetValue(current, out var targets)) {
                foreach (var target in targets)
                    queue.Enqueue(target);
            }
        }
        return false;
    }

    /// <summary>拓扑排序 — Kahn 算法(无锁快照)</summary>
    public IReadOnlyList<ImmutableDagNode<T>> TopologicalSort()
        => TopologicalSortCore(Volatile.Read(ref _state)).SelectMany(level => level).ToList();

    /// <summary>分层拓扑排序,同层可并行执行(无锁快照)</summary>
    public IReadOnlyList<IReadOnlyList<ImmutableDagNode<T>>> TopologicalSortByLevels()
        => TopologicalSortCore(Volatile.Read(ref _state));

    /// <summary>检测是否存在环(无锁快照)</summary>
    public bool HasCycle() {
        var state = Volatile.Read(ref _state);
        return TopologicalSortCore(state).SelectMany(level => level).Count() < state.Nodes.Count;
    }

    /// <summary>查找所有环路径(无锁快照)</summary>
    public IReadOnlyList<IReadOnlyList<string>> FindAllCycles() {
        var state = Volatile.Read(ref _state);
        var cycles = new List<IReadOnlyList<string>>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new HashSet<string>(StringComparer.Ordinal);
        var path = new List<string>();
        foreach (var nodeId in state.Nodes.Keys)
            DfsFindCycles(state, nodeId, visited, stack, path, cycles);
        return cycles;
    }

    /// <summary>获取节点的所有上游节点(依赖,无锁快照)</summary>
    public IEnumerable<ImmutableDagNode<T>> GetAncestors(string nodeId)
        => BfsCollect(Volatile.Read(ref _state), nodeId, true);

    /// <summary>获取节点的所有下游节点(受影响者,无锁快照)</summary>
    public IEnumerable<ImmutableDagNode<T>> GetDescendants(string nodeId)
        => BfsCollect(Volatile.Read(ref _state), nodeId, false);

    /// <summary>增量重算 — 从指定节点沿拓扑序获取受影响子图(无锁快照)</summary>
    public IEnumerable<ImmutableDagNode<T>> GetAffectedSubgraph(string changedNodeId) {
        var state = Volatile.Read(ref _state);
        var descendants = BfsCollect(state, changedNodeId, false).Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        descendants.Add(changedNodeId);
        var subgraphNodes = descendants.Select(id => state.Nodes[id]).ToList();
        var inDegree = subgraphNodes.ToDictionary(n => n.Id, _ => 0, StringComparer.Ordinal);
        foreach (var node in subgraphNodes) {
            foreach (var edgeId in node.InEdgeIds) {
                if (state.Edges.TryGetValue(edgeId, out var edge) && descendants.Contains(edge.FromId))
                    inDegree[node.Id]++;
            }
        }
        var queue = new Queue<string>();
        foreach (var kvp in inDegree)
            if (kvp.Value == 0) queue.Enqueue(kvp.Key);
        var result = new List<ImmutableDagNode<T>>();
        while (queue.Count > 0) {
            var id = queue.Dequeue();
            result.Add(state.Nodes[id]);
            if (!state.Adjacency.TryGetValue(id, out var targets)) continue;
            foreach (var targetId in targets) {
                if (!descendants.Contains(targetId)) continue;
                inDegree[targetId]--;
                if (inDegree[targetId] == 0) queue.Enqueue(targetId);
            }
        }
        return result;
    }

    /// <summary>清空所有节点和边(CAS 无锁)</summary>
    public void Clear() {
        while (true) {
            var current = Volatile.Read(ref _state);
            var newState = current with {
                Nodes = EmptyNodes,
                Edges = EmptyEdges,
                EdgesByEndpoints = EmptyEdgeIndex,
                Adjacency = EmptyAdj,
                ReverseAdjacency = EmptyAdj,
                Version = current.Version + 1
            };
            if (Interlocked.CompareExchange(ref _state, newState, current) == current)
                return;
        }
    }

    /// <summary>异步添加节点(CAS 无锁,直接包同步)</summary>
    public Task AddNodeAsync(ImmutableDagNode<T> node, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        AddNode(node);
        return Task.CompletedTask;
    }

    /// <summary>异步添加边,自动检测环(CAS 无锁,直接包同步)</summary>
    public Task AddEdgeAsync(DagEdge edge, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        AddEdge(edge);
        return Task.CompletedTask;
    }

    /// <summary>异步尝试添加边,允许产生环(CAS 无锁,直接包同步)</summary>
    public Task TryAddEdgeAsync(DagEdge edge, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        TryAddEdge(edge);
        return Task.CompletedTask;
    }

    /// <summary>异步移除节点(CAS 无锁,直接包同步)</summary>
    public Task RemoveNodeAsync(string nodeId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        RemoveNode(nodeId);
        return Task.CompletedTask;
    }

    /// <summary>异步移除边(CAS 无锁,直接包同步)</summary>
    public Task RemoveEdgeAsync(string edgeId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        RemoveEdge(edgeId);
        return Task.CompletedTask;
    }

    /// <summary>异步判断是否会产生环(CAS 无锁,直接包同步)</summary>
    public Task<bool> WouldCreateCycleAsync(string fromId, string toId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(WouldCreateCycle(fromId, toId));
    }

    private static DagState AddEdgeToState(DagState state, DagEdge edge) {
        var fromNode = state.Nodes[edge.FromId];
        var toNode = state.Nodes[edge.ToId];
        var newFromNode = fromNode with { OutEdgeIds = fromNode.OutEdgeIds.Add(edge.Id) };
        var newToNode = toNode with { InEdgeIds = toNode.InEdgeIds.Add(edge.Id) };
        var newNodes = state.Nodes.SetItem(edge.FromId, newFromNode).SetItem(edge.ToId, newToNode);
        var newEdges = state.Edges.SetItem(edge.Id, edge);
        var newEdgeIndex = state.EdgesByEndpoints.SetItem((edge.FromId, edge.ToId), edge.Id);
        var fromAdj = state.Adjacency.TryGetValue(edge.FromId, out var adj) ? adj : ImmutableHamTSet<string>.Empty;
        var newAdj = state.Adjacency.SetItem(edge.FromId, fromAdj.Add(edge.ToId));
        var toRevAdj = state.ReverseAdjacency.TryGetValue(edge.ToId, out var revAdj) ? revAdj : ImmutableHamTSet<string>.Empty;
        var newRevAdj = state.ReverseAdjacency.SetItem(edge.ToId, toRevAdj.Add(edge.FromId));
        return state with {
            Nodes = newNodes,
            Edges = newEdges,
            EdgesByEndpoints = newEdgeIndex,
            Adjacency = newAdj,
            ReverseAdjacency = newRevAdj,
            Version = state.Version + 1
        };
    }

    private static DagState RemoveNodeFromState(DagState state, string nodeId, ImmutableDagNode<T> node) {
        var newEdges = state.Edges;
        var newEdgeIndex = state.EdgesByEndpoints;
        var newNodes = state.Nodes;
        var newAdj = state.Adjacency;
        var newRevAdj = state.ReverseAdjacency;

        foreach (var edgeId in node.InEdgeIds.Concat(node.OutEdgeIds)) {
            if (!newEdges.TryGetValue(edgeId, out var edge)) continue;
            newEdges = newEdges.Remove(edgeId);
            newEdgeIndex = newEdgeIndex.Remove((edge.FromId, edge.ToId));
            if (newNodes.TryGetValue(edge.FromId, out var fromNode))
                newNodes = newNodes.SetItem(edge.FromId, fromNode with { OutEdgeIds = fromNode.OutEdgeIds.Remove(edgeId) });
            if (newNodes.TryGetValue(edge.ToId, out var toNode))
                newNodes = newNodes.SetItem(edge.ToId, toNode with { InEdgeIds = toNode.InEdgeIds.Remove(edgeId) });
            if (newAdj.TryGetValue(edge.FromId, out var adj))
                newAdj = newAdj.SetItem(edge.FromId, adj.Remove(edge.ToId));
            if (newRevAdj.TryGetValue(edge.ToId, out var revAdj))
                newRevAdj = newRevAdj.SetItem(edge.ToId, revAdj.Remove(edge.FromId));
        }

        newNodes = newNodes.Remove(nodeId);
        newAdj = newAdj.Remove(nodeId);
        newRevAdj = newRevAdj.Remove(nodeId);

        return state with {
            Nodes = newNodes,
            Edges = newEdges,
            EdgesByEndpoints = newEdgeIndex,
            Adjacency = newAdj,
            ReverseAdjacency = newRevAdj,
            Version = state.Version + 1
        };
    }

    private static DagState RemoveEdgeFromState(DagState state, string edgeId, DagEdge edge) {
        var fromNode = state.Nodes[edge.FromId];
        var toNode = state.Nodes[edge.ToId];
        var newNodes = state.Nodes
            .SetItem(edge.FromId, fromNode with { OutEdgeIds = fromNode.OutEdgeIds.Remove(edgeId) })
            .SetItem(edge.ToId, toNode with { InEdgeIds = toNode.InEdgeIds.Remove(edgeId) });
        var newEdges = state.Edges.Remove(edgeId);
        var newEdgeIndex = state.EdgesByEndpoints.Remove((edge.FromId, edge.ToId));
        var newAdj = state.Adjacency;
        if (newAdj.TryGetValue(edge.FromId, out var adj))
            newAdj = newAdj.SetItem(edge.FromId, adj.Remove(edge.ToId));
        var newRevAdj = state.ReverseAdjacency;
        if (newRevAdj.TryGetValue(edge.ToId, out var revAdj))
            newRevAdj = newRevAdj.SetItem(edge.ToId, revAdj.Remove(edge.FromId));
        return state with {
            Nodes = newNodes,
            Edges = newEdges,
            EdgesByEndpoints = newEdgeIndex,
            Adjacency = newAdj,
            ReverseAdjacency = newRevAdj,
            Version = state.Version + 1
        };
    }

    private static bool CanReach(DagState state, string from, string target) {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        queue.Enqueue(from);
        while (queue.Count > 0) {
            var current = queue.Dequeue();
            if (current == target) return true;
            if (!visited.Add(current)) continue;
            if (state.Adjacency.TryGetValue(current, out var neighbors)) {
                foreach (var n in neighbors)
                    queue.Enqueue(n);
            }
        }
        return false;
    }

    private static IReadOnlyList<IReadOnlyList<ImmutableDagNode<T>>> TopologicalSortCore(DagState state) {
        var inDegree = state.Nodes.Keys.ToDictionary(id => id, _ => 0, StringComparer.Ordinal);
        foreach (var edge in state.Edges.Values)
            inDegree[edge.ToId]++;
        var currentLevel = inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key).ToList();
        var result = new List<IReadOnlyList<ImmutableDagNode<T>>>();
        while (currentLevel.Count > 0) {
            result.Add(currentLevel.Select(id => state.Nodes[id]).ToList());
            var nextLevel = new List<string>();
            foreach (var id in currentLevel) {
                if (!state.Adjacency.TryGetValue(id, out var targets)) continue;
                foreach (var targetId in targets) {
                    inDegree[targetId]--;
                    if (inDegree[targetId] == 0) nextLevel.Add(targetId);
                }
            }
            currentLevel = nextLevel;
        }
        return result;
    }

    private static void DfsFindCycles(DagState state, string nodeId, HashSet<string> visited, HashSet<string> stack, List<string> path, List<IReadOnlyList<string>> cycles) {
        if (stack.Contains(nodeId)) {
            var cycleStart = path.IndexOf(nodeId);
            if (cycleStart >= 0) cycles.Add(path.Skip(cycleStart).ToList());
            return;
        }
        if (visited.Contains(nodeId)) return;
        visited.Add(nodeId);
        stack.Add(nodeId);
        path.Add(nodeId);
        if (state.Adjacency.TryGetValue(nodeId, out var neighbors)) {
            foreach (var next in neighbors)
                DfsFindCycles(state, next, visited, stack, path, cycles);
        }
        stack.Remove(nodeId);
        path.RemoveAt(path.Count - 1);
    }

    private static IEnumerable<ImmutableDagNode<T>> BfsCollect(DagState state, string startId, bool reverse) {
        var adjacency = reverse ? state.ReverseAdjacency : state.Adjacency;
        var result = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (adjacency.TryGetValue(startId, out var neighbors)) {
            foreach (var n in neighbors) queue.Enqueue(n);
        }
        while (queue.Count > 0) {
            var id = queue.Dequeue();
            if (!result.Add(id)) continue;
            if (adjacency.TryGetValue(id, out var next)) {
                foreach (var n in next) queue.Enqueue(n);
            }
        }
        return result.Select(id => state.Nodes[id]);
    }
}
