namespace Structura.Dag;

/// <summary>
/// 线程安全的 DAG — 无锁实现：写操作在私有副本上变更后 CAS 发布新快照，读操作直接读取当前快照。
/// <para>不变式：发布出去的 <see cref="Dag{T}"/> 快照永不再被修改（copy-on-write），
/// 因此读侧（Nodes/Edges/拓扑排序）无需任何同步即可安全并发遍历。</para>
/// <para>写竞争时 CAS 失败方按最新快照重新计算（无阻塞、无超时、无死锁）。</para>
/// </summary>
public sealed class ConcurrentDag<T> : IDisposable {
    private Dag<T> _current = new();
    private int _disposed;

    /// <summary>所有节点的只读快照(无锁,反映当前内部状态)</summary>
    public IReadOnlyDictionary<string, DagNode<T>> Nodes => Volatile.Read(ref _current).Nodes;
    /// <summary>所有边的只读快照(无锁,反映当前内部状态)</summary>
    public IReadOnlyDictionary<string, DagEdge> Edges => Volatile.Read(ref _current).Edges;
    /// <summary>图版本号(无锁读取)</summary>
    public int Version => Volatile.Read(ref _current).Version;

    /// <summary>
    /// 按端点 (fromId, toId) O(1) 查找边(无锁读取快照)，替代 Edges.Values 线性扫描
    /// </summary>
    public bool TryGetEdge(string fromId, string toId, [MaybeNullWhen(false)] out DagEdge edge)
        => Volatile.Read(ref _current).TryGetEdge(fromId, toId, out edge);

    /// <summary>
    /// 在私有副本上执行变更并 CAS 发布 — 变更失败（业务拒绝）不发布副本。
    /// </summary>
    private DagResult Mutate(Func<Dag<T>, DagResult> mutation) {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        while (true) {
            var snapshot = Volatile.Read(ref _current);
            var next = snapshot.CloneSnapshot();
            var result = mutation(next);
            if (!result.Success) return result;
            if (Interlocked.CompareExchange(ref _current, next, snapshot) == snapshot) return result;
        }
    }

    /// <summary>在私有副本上执行查询 — 查询只读当前快照</summary>
    private TResult Query<TResult>(Func<Dag<T>, TResult> query) => query(Volatile.Read(ref _current));

    /// <summary>添加节点;节点已存在返回失败</summary>
    /// <param name="node">要添加的节点</param>
    /// <returns>操作结果</returns>
    public DagResult AddNode(DagNode<T> node) => Mutate(dag => dag.AddNode(node));

    /// <summary>添加边(自动检测环);成环返回失败</summary>
    /// <param name="edge">要添加的边</param>
    /// <returns>操作结果</returns>
    public DagResult AddEdge(DagEdge edge) => Mutate(dag => dag.AddEdge(edge));

    /// <summary>尝试添加边(允许产生环)</summary>
    /// <param name="edge">要添加的边</param>
    /// <returns>操作结果</returns>
    public DagResult TryAddEdge(DagEdge edge) => Mutate(dag => dag.TryAddEdge(edge));

    /// <summary>移除节点及其关联边</summary>
    /// <param name="nodeId">要移除的节点 ID</param>
    /// <returns>操作结果</returns>
    public DagResult RemoveNode(string nodeId) => Mutate(dag => dag.RemoveNode(nodeId));

    /// <summary>移除边(保留节点)</summary>
    /// <param name="edgeId">要移除的边 ID</param>
    /// <returns>操作结果</returns>
    public DagResult RemoveEdge(string edgeId) => Mutate(dag => dag.RemoveEdge(edgeId));

    /// <summary>判断添加 from→to 边是否会产生环</summary>
    /// <param name="fromId">源节点 ID</param>
    /// <param name="toId">目标节点 ID</param>
    /// <returns>会产生环返回 true</returns>
    public bool WouldCreateCycle(string fromId, string toId) => Query(dag => dag.WouldCreateCycle(fromId, toId));

    /// <summary>异步添加节点</summary>
    /// <param name="node">要添加的节点</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    public Task<DagResult> AddNodeAsync(DagNode<T> node, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(AddNode(node));
    }

    /// <summary>异步添加边(自动检测环)</summary>
    /// <param name="edge">要添加的边</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    public Task<DagResult> AddEdgeAsync(DagEdge edge, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(AddEdge(edge));
    }

    /// <summary>异步尝试添加边(允许产生环)</summary>
    /// <param name="edge">要添加的边</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    public Task<DagResult> TryAddEdgeAsync(DagEdge edge, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(TryAddEdge(edge));
    }

    /// <summary>异步移除节点及其关联边</summary>
    /// <param name="nodeId">要移除的节点 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    public Task<DagResult> RemoveNodeAsync(string nodeId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(RemoveNode(nodeId));
    }

    /// <summary>异步移除边(保留节点)</summary>
    /// <param name="edgeId">要移除的边 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>操作结果</returns>
    public Task<DagResult> RemoveEdgeAsync(string edgeId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(RemoveEdge(edgeId));
    }

    /// <summary>异步判断添加 from→to 边是否会产生环</summary>
    /// <param name="fromId">源节点 ID</param>
    /// <param name="toId">目标节点 ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>会产生环返回 true</returns>
    public Task<bool> WouldCreateCycleAsync(string fromId, string toId, CancellationToken ct = default) {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(WouldCreateCycle(fromId, toId));
    }

    /// <summary>拓扑排序(Kahn 算法)</summary>
    /// <returns>拓扑序节点列表</returns>
    public IReadOnlyList<DagNode<T>> TopologicalSort() => Query(dag => dag.TopologicalSort());

    /// <summary>分层拓扑排序;同层节点可并行执行</summary>
    /// <returns>按层级分组的节点列表</returns>
    public IReadOnlyList<IReadOnlyList<DagNode<T>>> TopologicalSortByLevels() => Query(dag => dag.TopologicalSortByLevels());

    /// <summary>检测是否存在环</summary>
    /// <returns>存在环返回 true</returns>
    public bool HasCycle() => Query(dag => dag.HasCycle());

    /// <summary>查找所有环路径</summary>
    /// <returns>环路径列表</returns>
    public IReadOnlyList<IReadOnlyList<string>> FindAllCycles() => Query(dag => dag.FindAllCycles());

    /// <summary>获取节点的所有上游节点(依赖)</summary>
    /// <param name="nodeId">起始节点 ID</param>
    /// <returns>上游节点集合</returns>
    public IEnumerable<DagNode<T>> GetAncestors(string nodeId) => Query(dag => dag.GetAncestors(nodeId).ToList());

    /// <summary>获取节点的所有下游节点(受影响者)</summary>
    /// <param name="nodeId">起始节点 ID</param>
    /// <returns>下游节点集合</returns>
    public IEnumerable<DagNode<T>> GetDescendants(string nodeId) => Query(dag => dag.GetDescendants(nodeId).ToList());

    /// <summary>增量重算 — 从指定节点开始沿拓扑序获取受影响子图</summary>
    /// <param name="changedNodeId">发生变更的节点 ID</param>
    /// <returns>受影响子图的拓扑序节点序列</returns>
    public IEnumerable<DagNode<T>> GetAffectedSubgraph(string changedNodeId) => Query(dag => dag.GetAffectedSubgraph(changedNodeId).ToList());

    /// <summary>清空所有节点和边</summary>
    public void Clear() => Mutate(dag => {
        foreach (var nodeId in dag.Nodes.Keys.ToList())
            dag.RemoveNode(nodeId);
        return DagResult.Ok();
    });

    /// <summary>释放实例（幂等）— 无锁实现无内部资源，保留 IDisposable 契约供 using 消费。</summary>
    public void Dispose() => Interlocked.Exchange(ref _disposed, 1);
}
