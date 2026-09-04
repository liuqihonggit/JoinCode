namespace Structura.Dag;

/// <summary>
/// 线程安全的 DAG — 所有写操作加锁保护，读操作无锁（快照）
/// </summary>
public sealed class ConcurrentDag<T> : IDisposable
{
    private readonly Dag<T> _inner = new();
    private readonly AsyncLock _lock = new();

    public IReadOnlyDictionary<string, DagNode<T>> Nodes => _inner.Nodes;
    public IReadOnlyDictionary<string, DagEdge> Edges => _inner.Edges;
    public int Version => _inner.Version;

    /// <summary>
    /// 在锁保护下执行有返回值的操作；超时返回 <paramref name="timeoutResult"/>
    /// </summary>
    private TResult WithLock<TResult>(Func<TResult> action, TResult timeoutResult)
    {
        using var guard = _lock.TryLock();
        if (guard is null) return timeoutResult;
        return action();
    }

    /// <summary>
    /// 在锁保护下执行无返回值的操作；超时直接返回
    /// </summary>
    private void WithLock(Action action)
    {
        using var guard = _lock.TryLock();
        if (guard is null) return;
        action();
    }

    public DagResult AddNode(DagNode<T> node)
        => WithLock(() => _inner.AddNode(node), DagResult.Fail("Lock timeout"));

    public DagResult AddEdge(DagEdge edge)
        => WithLock(() => _inner.AddEdge(edge), DagResult.Fail("Lock timeout"));

    public DagResult TryAddEdge(DagEdge edge)
        => WithLock(() => _inner.TryAddEdge(edge), DagResult.Fail("Lock timeout"));

    public DagResult RemoveNode(string nodeId)
        => WithLock(() => _inner.RemoveNode(nodeId), DagResult.Fail("Lock timeout"));

    public DagResult RemoveEdge(string edgeId)
        => WithLock(() => _inner.RemoveEdge(edgeId), DagResult.Fail("Lock timeout"));

    public bool WouldCreateCycle(string fromId, string toId)
        => WithLock(() => _inner.WouldCreateCycle(fromId, toId), false);

    public Task<DagResult> AddNodeAsync(DagNode<T> node, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.AddNode(node));
    }

    public Task<DagResult> AddEdgeAsync(DagEdge edge, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.AddEdge(edge));
    }

    public Task<DagResult> TryAddEdgeAsync(DagEdge edge, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.TryAddEdge(edge));
    }

    public Task<DagResult> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.RemoveNode(nodeId));
    }

    public Task<DagResult> RemoveEdgeAsync(string edgeId, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.RemoveEdge(edgeId));
    }

    public Task<bool> WouldCreateCycleAsync(string fromId, string toId, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.WouldCreateCycle(fromId, toId));
    }

    public IReadOnlyList<DagNode<T>> TopologicalSort()
        => WithLock(() => _inner.TopologicalSort(), Array.Empty<DagNode<T>>());

    public IReadOnlyList<IReadOnlyList<DagNode<T>>> TopologicalSortByLevels()
        => WithLock(() => _inner.TopologicalSortByLevels(), Array.Empty<IReadOnlyList<DagNode<T>>>());

    public bool HasCycle()
        => WithLock(() => _inner.HasCycle(), false);

    public IReadOnlyList<IReadOnlyList<string>> FindAllCycles()
        => WithLock(() => _inner.FindAllCycles(), Array.Empty<IReadOnlyList<string>>());

    public IEnumerable<DagNode<T>> GetAncestors(string nodeId)
        => WithLock(() => _inner.GetAncestors(nodeId).ToList(), []);

    public IEnumerable<DagNode<T>> GetDescendants(string nodeId)
        => WithLock(() => _inner.GetDescendants(nodeId).ToList(), []);

    public IEnumerable<DagNode<T>> GetAffectedSubgraph(string changedNodeId)
        => WithLock(() => _inner.GetAffectedSubgraph(changedNodeId).ToList(), []);

    public void Clear()
        => WithLock(() =>
        {
            foreach (var nodeId in _inner.Nodes.Keys.ToList())
                _inner.RemoveNode(nodeId);
        });

    public void Dispose()
    {
        _lock.Dispose();
    }
}
