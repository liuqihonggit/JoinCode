namespace Structura.Dag;

/// <summary>
/// 线程安全的 DAG — 所有写操作加锁保护，读操作无锁（快照）
/// </summary>
public sealed class ConcurrentDag<T> : IDisposable
{
    private readonly Dag<T> _inner = new();
    private readonly AsyncLock _lock = new();

    /// <summary>所有节点的只读快照(无锁,反映当前内部状态)</summary>
    public IReadOnlyDictionary<string, DagNode<T>> Nodes => _inner.Nodes;
    /// <summary>所有边的只读快照(无锁,反映当前内部状态)</summary>
    public IReadOnlyDictionary<string, DagEdge> Edges => _inner.Edges;
    /// <summary>图版本号(无锁读取)</summary>
    public int Version => _inner.Version;

    /// <summary>
    /// 按端点 (fromId, toId) O(1) 查找边(无锁读取快照)，替代 Edges.Values 线性扫描
    /// </summary>
    public bool TryGetEdge(string fromId, string toId, [MaybeNullWhen(false)] out DagEdge edge) => _inner.TryGetEdge(fromId, toId, out edge);

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

    /// <summary>在锁保护下添加节点;锁超时返回失败</summary>
    /// <param name="node">要添加的节点</param>
    /// <returns>操作结果;锁超时返回 ErrorMessage="Lock timeout"</returns>
    public DagResult AddNode(DagNode<T> node)
        => WithLock(() => _inner.AddNode(node), DagResult.Fail("Lock timeout"));

    /// <summary>在锁保护下添加边(自动检测环);锁超时返回失败</summary>
    /// <param name="edge">要添加的边</param>
    /// <returns>操作结果;锁超时返回 ErrorMessage="Lock timeout"</returns>
    public DagResult AddEdge(DagEdge edge)
        => WithLock(() => _inner.AddEdge(edge), DagResult.Fail("Lock timeout"));

    /// <summary>在锁保护下尝试添加边(允许产生环);锁超时返回失败</summary>
    /// <param name="edge">要添加的边</param>
    /// <returns>操作结果;锁超时返回 ErrorMessage="Lock timeout"</returns>
    public DagResult TryAddEdge(DagEdge edge)
        => WithLock(() => _inner.TryAddEdge(edge), DagResult.Fail("Lock timeout"));

    /// <summary>在锁保护下移除节点及其关联边;锁超时返回失败</summary>
    /// <param name="nodeId">要移除的节点 ID</param>
    /// <returns>操作结果;锁超时返回 ErrorMessage="Lock timeout"</returns>
    public DagResult RemoveNode(string nodeId)
        => WithLock(() => _inner.RemoveNode(nodeId), DagResult.Fail("Lock timeout"));

    /// <summary>在锁保护下移除边(保留节点);锁超时返回失败</summary>
    /// <param name="edgeId">要移除的边 ID</param>
    /// <returns>操作结果;锁超时返回 ErrorMessage="Lock timeout"</returns>
    public DagResult RemoveEdge(string edgeId)
        => WithLock(() => _inner.RemoveEdge(edgeId), DagResult.Fail("Lock timeout"));

    /// <summary>在锁保护下判断添加 from→to 边是否会产生环;锁超时返回 false</summary>
    /// <param name="fromId">源节点 ID</param>
    /// <param name="toId">目标节点 ID</param>
    /// <returns>会产生环返回 true;锁超时返回 false</returns>
    public bool WouldCreateCycle(string fromId, string toId)
        => WithLock(() => _inner.WouldCreateCycle(fromId, toId), false);

    /// <summary>异步添加节点;锁等待超时抛出 TimeoutException</summary>
    /// <param name="node">要添加的节点</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    /// <exception cref="TimeoutException">锁等待超时</exception>
    public Task<DagResult> AddNodeAsync(DagNode<T> node, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.AddNode(node));
    }

    /// <summary>异步添加边(自动检测环);锁等待超时抛出 TimeoutException</summary>
    /// <param name="edge">要添加的边</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    /// <exception cref="TimeoutException">锁等待超时</exception>
    public Task<DagResult> AddEdgeAsync(DagEdge edge, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.AddEdge(edge));
    }

    /// <summary>异步尝试添加边(允许产生环);锁等待超时抛出 TimeoutException</summary>
    /// <param name="edge">要添加的边</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    /// <exception cref="TimeoutException">锁等待超时</exception>
    public Task<DagResult> TryAddEdgeAsync(DagEdge edge, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.TryAddEdge(edge));
    }

    /// <summary>异步移除节点及其关联边;锁等待超时抛出 TimeoutException</summary>
    /// <param name="nodeId">要移除的节点 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    /// <exception cref="TimeoutException">锁等待超时</exception>
    public Task<DagResult> RemoveNodeAsync(string nodeId, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.RemoveNode(nodeId));
    }

    /// <summary>异步移除边(保留节点);锁等待超时抛出 TimeoutException</summary>
    /// <param name="edgeId">要移除的边 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    /// <exception cref="TimeoutException">锁等待超时</exception>
    public Task<DagResult> RemoveEdgeAsync(string edgeId, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.RemoveEdge(edgeId));
    }

    /// <summary>异步判断添加 from→to 边是否会产生环;锁等待超时抛出 TimeoutException</summary>
    /// <param name="fromId">源节点 ID</param>
    /// <param name="toId">目标节点 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>会产生环返回 true,否则 false</returns>
    /// <exception cref="TimeoutException">锁等待超时</exception>
    public Task<bool> WouldCreateCycleAsync(string fromId, string toId, CancellationToken ct = default)
    {
        using var guard = _lock.TryLock(ct) ?? throw new System.TimeoutException($"锁 '{_lock.Name}' DAG 等待超时");
        return Task.FromResult(_inner.WouldCreateCycle(fromId, toId));
    }

    /// <summary>在锁保护下执行拓扑排序(Kahn 算法);锁超时返回空数组</summary>
    /// <returns>拓扑序节点列表;锁超时返回空数组</returns>
    public IReadOnlyList<DagNode<T>> TopologicalSort()
        => WithLock(() => _inner.TopologicalSort(), Array.Empty<DagNode<T>>());

    /// <summary>在锁保护下分层拓扑排序;同层节点可并行执行;锁超时返回空数组</summary>
    /// <returns>按层级分组的节点列表;锁超时返回空数组</returns>
    public IReadOnlyList<IReadOnlyList<DagNode<T>>> TopologicalSortByLevels()
        => WithLock(() => _inner.TopologicalSortByLevels(), Array.Empty<IReadOnlyList<DagNode<T>>>());

    /// <summary>在锁保护下检测是否存在环;锁超时返回 false</summary>
    /// <returns>存在环返回 true;锁超时返回 false</returns>
    public bool HasCycle()
        => WithLock(() => _inner.HasCycle(), false);

    /// <summary>在锁保护下查找所有环路径;锁超时返回空数组</summary>
    /// <returns>环路径列表;锁超时返回空数组</returns>
    public IReadOnlyList<IReadOnlyList<string>> FindAllCycles()
        => WithLock(() => _inner.FindAllCycles(), Array.Empty<IReadOnlyList<string>>());

    /// <summary>在锁保护下获取节点的所有上游节点(依赖);锁超时返回空集合</summary>
    /// <param name="nodeId">起始节点 ID</param>
    /// <returns>上游节点集合;锁超时返回空集合</returns>
    public IEnumerable<DagNode<T>> GetAncestors(string nodeId)
        => WithLock(() => _inner.GetAncestors(nodeId).ToList(), []);

    /// <summary>在锁保护下获取节点的所有下游节点(受影响者);锁超时返回空集合</summary>
    /// <param name="nodeId">起始节点 ID</param>
    /// <returns>下游节点集合;锁超时返回空集合</returns>
    public IEnumerable<DagNode<T>> GetDescendants(string nodeId)
        => WithLock(() => _inner.GetDescendants(nodeId).ToList(), []);

    /// <summary>在锁保护下增量重算 — 从指定节点开始沿拓扑序获取受影响子图;锁超时返回空集合</summary>
    /// <param name="changedNodeId">发生变更的节点 ID</param>
    /// <returns>受影响子图的拓扑序节点序列;锁超时返回空集合</returns>
    public IEnumerable<DagNode<T>> GetAffectedSubgraph(string changedNodeId)
        => WithLock(() => _inner.GetAffectedSubgraph(changedNodeId).ToList(), []);

    /// <summary>在锁保护下清空所有节点和边;锁超时静默返回</summary>
    public void Clear()
        => WithLock(() =>
        {
            foreach (var nodeId in _inner.Nodes.Keys.ToList())
                _inner.RemoveNode(nodeId);
        });

    /// <summary>释放内部锁资源</summary>
    public void Dispose()
    {
        _lock.Dispose();
    }
}
