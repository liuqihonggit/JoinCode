namespace Structura.Dag;

/// <summary>
/// 线程安全的 DAG — 所有写操作加锁保护，读操作无锁（快照）
/// </summary>
public sealed class ConcurrentDag<T> : IDisposable
{
    private readonly Dag<T> _inner = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public IReadOnlyDictionary<string, DagNode<T>> Nodes => _inner.Nodes;
    public IReadOnlyDictionary<string, DagEdge> Edges => _inner.Edges;
    public int Version => _inner.Version;

    public DagResult AddNode(DagNode<T> node)
    {
        _lock.Wait();
        try { return _inner.AddNode(node); }
        finally { _lock.Release(); }
    }

    public DagResult AddEdge(DagEdge edge)
    {
        _lock.Wait();
        try { return _inner.AddEdge(edge); }
        finally { _lock.Release(); }
    }

    public DagResult TryAddEdge(DagEdge edge)
    {
        _lock.Wait();
        try { return _inner.TryAddEdge(edge); }
        finally { _lock.Release(); }
    }

    public DagResult RemoveNode(string nodeId)
    {
        _lock.Wait();
        try { return _inner.RemoveNode(nodeId); }
        finally { _lock.Release(); }
    }

    public DagResult RemoveEdge(string edgeId)
    {
        _lock.Wait();
        try { return _inner.RemoveEdge(edgeId); }
        finally { _lock.Release(); }
    }

    public bool WouldCreateCycle(string fromId, string toId)
    {
        _lock.Wait();
        try { return _inner.WouldCreateCycle(fromId, toId); }
        finally { _lock.Release(); }
    }

    public async Task<DagResult> AddNodeAsync(DagNode<T> node, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { return _inner.AddNode(node); }
        finally { _lock.Release(); }
    }

    public async Task<DagResult> AddEdgeAsync(DagEdge edge, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { return _inner.AddEdge(edge); }
        finally { _lock.Release(); }
    }

    public async Task<DagResult> TryAddEdgeAsync(DagEdge edge, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { return _inner.TryAddEdge(edge); }
        finally { _lock.Release(); }
    }

    public async Task<DagResult> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { return _inner.RemoveNode(nodeId); }
        finally { _lock.Release(); }
    }

    public async Task<DagResult> RemoveEdgeAsync(string edgeId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { return _inner.RemoveEdge(edgeId); }
        finally { _lock.Release(); }
    }

    public async Task<bool> WouldCreateCycleAsync(string fromId, string toId, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try { return _inner.WouldCreateCycle(fromId, toId); }
        finally { _lock.Release(); }
    }

    public IReadOnlyList<DagNode<T>> TopologicalSort()
    {
        _lock.Wait();
        try { return _inner.TopologicalSort(); }
        finally { _lock.Release(); }
    }

    public IReadOnlyList<IReadOnlyList<DagNode<T>>> TopologicalSortByLevels()
    {
        _lock.Wait();
        try { return _inner.TopologicalSortByLevels(); }
        finally { _lock.Release(); }
    }

    public bool HasCycle()
    {
        _lock.Wait();
        try { return _inner.HasCycle(); }
        finally { _lock.Release(); }
    }

    public IReadOnlyList<IReadOnlyList<string>> FindAllCycles()
    {
        _lock.Wait();
        try { return _inner.FindAllCycles(); }
        finally { _lock.Release(); }
    }

    public IReadOnlyList<DagNode<T>> GetAncestors(string nodeId)
    {
        _lock.Wait();
        try { return _inner.GetAncestors(nodeId); }
        finally { _lock.Release(); }
    }

    public IReadOnlyList<DagNode<T>> GetDescendants(string nodeId)
    {
        _lock.Wait();
        try { return _inner.GetDescendants(nodeId); }
        finally { _lock.Release(); }
    }

    public IReadOnlyList<DagNode<T>> GetAffectedSubgraph(string changedNodeId)
    {
        _lock.Wait();
        try { return _inner.GetAffectedSubgraph(changedNodeId); }
        finally { _lock.Release(); }
    }

    public void Clear()
    {
        _lock.Wait();
        try
        {
            foreach (var nodeId in _inner.Nodes.Keys.ToList())
                _inner.RemoveNode(nodeId);
        }
        finally { _lock.Release(); }
    }

    public void Dispose()
    {
        _lock.Dispose();
    }
}
