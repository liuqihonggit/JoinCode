// JCC11005 抑制: EnterScope 返回 null 表示不在等待图作用域内, 是 Actor Ask 模式的合理语义
#pragma warning disable JCC11005
namespace Core.Utils;

/// <summary>
/// Ask 等待图跟踪器 — 封装等待图 DAG 的克隆、加边、环检测与 AsyncLocal 作用域管理(关注点分离)。
/// <para>原 ActorBase.EnterWaitGraph/CloneWaitGraph/WaitGraphScope 逻辑抽离至此。</para>
/// <para>等待图用 AsyncLocal 存储调用链本地图,避免全局静态图跨调用链污染/并发覆盖(Bug1 修复)。</para>
/// <para>每次 EnterScope 创建新 ImmutableDag 副本(从父图无锁快照复制),各异步流独立不竞态(P1-3 修复)。</para>
/// <para>职责边界:只维护等待图结构与作用域,不涉及超时/取消(由 ActorBase.AskAwait 负责)。</para>
/// <para>⚠️ AsyncLocal 字段保留在 ActorBase(供反射测试验证 Bug1/P1-3 修复),通过构造注入本跟踪器操作。</para>
/// </summary>
internal sealed class AskWaitGraphTracker {
    private readonly AsyncLocal<ImmutableDag<string>?> _waitGraph;

    /// <summary>初始化等待图跟踪器</summary>
    /// <param name="waitGraph">AsyncLocal 等待图存储(由 ActorBase 持有静态字段注入,保留反射可见性)</param>
    public AskWaitGraphTracker(AsyncLocal<ImmutableDag<string>?> waitGraph) {
        _waitGraph = waitGraph;
    }

    /// <summary>
    /// 进入等待图作用域 — 在当前异步流的调用链本地图加边 callerId→selfId,检测环,返回 scope(Dispose 恢复父图)。
    /// <para>callerId 为 null 或等于自身时返回 null(无需加边)。</para>
    /// <para>检测到环(加边失败抛 InvalidOperationException) → 转抛 ActorCyclicAskException。</para>
    /// </summary>
    /// <param name="callerId">调用方 Actor ID(null=无调用方上下文)</param>
    /// <param name="selfId">当前 Actor ID</param>
    /// <returns>等待图作用域(Dispose 恢复父图);null=无需加边</returns>
    /// <exception cref="ActorCyclicAskException">等待图检测到环 — 循环 Ask 死锁</exception>
    public WaitGraphScope? EnterScope(string? callerId, string selfId) {
        if (callerId is null || callerId == selfId) return null;
        var previousGraph = _waitGraph.Value;
        var graph = Clone(previousGraph);
        _waitGraph.Value = graph;
        if (!graph.Nodes.ContainsKey(callerId))
            graph.AddNode(new ImmutableDagNode<string> { Id = callerId, Payload = callerId });
        if (!graph.Nodes.ContainsKey(selfId))
            graph.AddNode(new ImmutableDagNode<string> { Id = selfId, Payload = selfId });
        var edge = new DagEdge { FromId = callerId, ToId = selfId };
        try {
            graph.AddEdge(edge);
        } catch (InvalidOperationException) {
            throw new ActorCyclicAskException(callerId, selfId);
        }
        return new WaitGraphScope(_waitGraph, previousGraph);
    }

    /// <summary>从源图无锁快照复制所有节点和边到新 ImmutableDag 实例(各异步流独立副本)</summary>
    private static ImmutableDag<string> Clone(ImmutableDag<string>? source) {
        if (source is null) return new ImmutableDag<string>();
        var dag = new ImmutableDag<string>();
        foreach (var node in source.Nodes.Values)
            dag.AddNode(node);
        foreach (var edge in source.Edges.Values)
            dag.TryAddEdge(edge);
        return dag;
    }

    /// <summary>等待图作用域 — Dispose 时恢复父图引用(P1-3: 每次创建副本,无需 RemoveEdge)</summary>
    internal sealed class WaitGraphScope(AsyncLocal<ImmutableDag<string>?> store, ImmutableDag<string>? previousGraph) : IDisposable {
        /// <summary>释放资源 — 恢复父图引用</summary>
        public void Dispose() => store.Value = previousGraph;
    }
}
