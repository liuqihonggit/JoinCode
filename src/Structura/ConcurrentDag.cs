namespace Structura.Dag;

/// <summary>
/// 线程安全的 DAG — 所有写操作加锁保护，读操作无锁（快照）
/// </summary>
public sealed class ConcurrentDag<T> : IDisposable
{
    private readonly Dag<T> _inner = new();
    private readonly SemaphoreSlim _lock = new(1, 1);
    // P1-9: 信号量等待超时 — 防止持有方异常未释放导致永久阻塞
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(5);

    public IReadOnlyDictionary<string, DagNode<T>> Nodes => _inner.Nodes;
    public IReadOnlyDictionary<string, DagEdge> Edges => _inner.Edges;
    public int Version => _inner.Version;

    /// <summary>
    /// 在锁保护下执行有返回值的操作；超时返回 <paramref name="timeoutResult"/>
    /// </summary>
    private TResult WithLock<TResult>(Func<TResult> action, TResult timeoutResult)
    {
        if (!_lock.Wait(LockTimeout)) return timeoutResult;
        try { return action(); }
        finally { _lock.Release(); }
    }

    /// <summary>
    /// 在锁保护下执行无返回值的操作；超时直接返回
    /// </summary>
    private void WithLock(Action action)
    {
        if (!_lock.Wait(LockTimeout)) return;
        try { action(); }
        finally { _lock.Release(); }
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

    // 超时返回 false（保守假设无环，避免阻塞调用方）
    public bool WouldCreateCycle(string fromId, string toId)
        => WithLock(() => _inner.WouldCreateCycle(fromId, toId), false);

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

    // 超时返回空列表，避免阻塞调用方
    public IReadOnlyList<DagNode<T>> TopologicalSort()
        => WithLock(() => _inner.TopologicalSort(), Array.Empty<DagNode<T>>());

    public IReadOnlyList<IReadOnlyList<DagNode<T>>> TopologicalSortByLevels()
        => WithLock(() => _inner.TopologicalSortByLevels(), Array.Empty<IReadOnlyList<DagNode<T>>>());

    public bool HasCycle()
        => WithLock(() => _inner.HasCycle(), false);

    public IReadOnlyList<IReadOnlyList<string>> FindAllCycles()
        => WithLock(() => _inner.FindAllCycles(), Array.Empty<IReadOnlyList<string>>());

    public IReadOnlyList<DagNode<T>> GetAncestors(string nodeId)
        => WithLock(() => _inner.GetAncestors(nodeId), Array.Empty<DagNode<T>>());

    public IReadOnlyList<DagNode<T>> GetDescendants(string nodeId)
        => WithLock(() => _inner.GetDescendants(nodeId), Array.Empty<DagNode<T>>());

    public IReadOnlyList<DagNode<T>> GetAffectedSubgraph(string changedNodeId)
        => WithLock(() => _inner.GetAffectedSubgraph(changedNodeId), Array.Empty<DagNode<T>>());

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
