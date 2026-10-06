namespace Structura.Tests;

/// <summary>
/// ConcurrentDag&lt;T&gt; 确定性单元测试 — 单线程下同步/异步 API 与 Dag&lt;T&gt; 行为一致。
/// 不写依赖线程调度的并发竞态测试,只写确定性单线程对拍与 Dispose 行为。
/// </summary>
public class ConcurrentDagTests {
    private static DagNode<string> Node(string id) => new() { Id = id, Payload = id };
    private static DagEdge Edge(string from, string to) => new() { FromId = from, ToId = to };

    private static ConcurrentDag<string> Build(params (string from, string to)[] edges) {
        var dag = new ConcurrentDag<string>();
        var nodeIds = new HashSet<string>();
        foreach (var (f, t) in edges) { nodeIds.Add(f); nodeIds.Add(t); }
        foreach (var id in nodeIds) dag.AddNode(Node(id));
        foreach (var (f, t) in edges) dag.AddEdge(Edge(f, t));
        return dag;
    }

    // ===== 同步 API 与 Dag 一致 =====

    [Fact]
    public void AddNode_Sync_Works() {
        using var dag = new ConcurrentDag<string>();
        var result = dag.AddNode(Node("a"));
        result.Success.Should().BeTrue();
        dag.Nodes.Count.Should().Be(1);
    }

    [Fact]
    public void AddNode_Sync_Duplicate_ReturnsFail() {
        using var dag = new ConcurrentDag<string>();
        dag.AddNode(Node("a"));
        var result = dag.AddNode(Node("a"));
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void AddEdge_Sync_Works() {
        using var dag = Build(("a", "b"));
        dag.Edges.Count.Should().Be(1);
    }

    [Fact]
    public void AddEdge_Sync_Cycle_ReturnsFail() {
        using var dag = Build(("a", "b"));
        var result = dag.AddEdge(Edge("b", "a"));
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void TryAddEdge_Sync_AllowsCycle() {
        using var dag = new ConcurrentDag<string>();
        dag.AddNode(Node("a"));
        dag.AddNode(Node("b"));
        dag.AddEdge(Edge("a", "b"));
        var result = dag.TryAddEdge(Edge("b", "a"));
        result.Success.Should().BeTrue();
        dag.Edges.Count.Should().Be(2);
    }

    [Fact]
    public void RemoveNode_Sync_Works() {
        using var dag = Build(("a", "b"), ("b", "c"));
        var result = dag.RemoveNode("b");
        result.Success.Should().BeTrue();
        dag.Nodes.Count.Should().Be(2);
        dag.Edges.Count.Should().Be(0);
    }

    [Fact]
    public void RemoveEdge_Sync_Works() {
        using var dag = Build(("a", "b"));
        var edgeId = dag.Edges.Values.First().Id;
        var result = dag.RemoveEdge(edgeId);
        result.Success.Should().BeTrue();
        dag.Edges.Count.Should().Be(0);
    }

    [Fact]
    public void TopologicalSort_Sync_MatchesDag() {
        using var cdag = Build(("a", "b"), ("a", "c"), ("b", "d"), ("c", "d"));
        var levels = cdag.TopologicalSortByLevels();
        levels.Should().HaveCount(3);
        levels[0].Select(n => n.Id).Should().BeEquivalentTo(new[] { "a" });
        levels[1].Select(n => n.Id).Should().BeEquivalentTo(new[] { "b", "c" });
        levels[2].Select(n => n.Id).Should().BeEquivalentTo(new[] { "d" });
    }

    [Fact]
    public void HasCycle_Sync_True() {
        using var dag = new ConcurrentDag<string>();
        foreach (var id in new[] { "a", "b", "c" }) dag.AddNode(Node(id));
        dag.TryAddEdge(Edge("a", "b"));
        dag.TryAddEdge(Edge("b", "c"));
        dag.TryAddEdge(Edge("c", "a"));
        dag.HasCycle().Should().BeTrue();
    }

    [Fact]
    public void HasCycle_Sync_False() {
        using var dag = Build(("a", "b"), ("b", "c"));
        dag.HasCycle().Should().BeFalse();
    }

    [Fact]
    public void FindAllCycles_Sync_ReturnsCycles() {
        using var dag = new ConcurrentDag<string>();
        foreach (var id in new[] { "a", "b" }) dag.AddNode(Node(id));
        dag.TryAddEdge(Edge("a", "b"));
        dag.TryAddEdge(Edge("b", "a"));
        var cycles = dag.FindAllCycles();
        cycles.Should().HaveCount(1);
        cycles[0].Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void GetAncestors_Sync_Works() {
        using var dag = Build(("a", "b"), ("b", "c"));
        dag.GetAncestors("c").Select(n => n.Id).Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void GetDescendants_Sync_Works() {
        using var dag = Build(("a", "b"), ("b", "c"));
        dag.GetDescendants("a").Select(n => n.Id).Should().BeEquivalentTo(new[] { "b", "c" });
    }

    [Fact]
    public void GetAffectedSubgraph_Sync_Works() {
        using var dag = Build(("a", "b"), ("b", "c"));
        dag.GetAffectedSubgraph("a").Select(n => n.Id).Should().ContainInOrder("a", "b", "c");
    }

    [Fact]
    public void WouldCreateCycle_Sync_Works() {
        using var dag = Build(("a", "b"));
        dag.WouldCreateCycle("b", "a").Should().BeTrue();
        dag.WouldCreateCycle("a", "b").Should().BeFalse();
    }

    [Fact]
    public void TryGetEdge_Works() {
        using var dag = Build(("a", "b"));
        dag.TryGetEdge("a", "b", out var edge).Should().BeTrue();
        edge!.FromId.Should().Be("a");
        dag.TryGetEdge("b", "a", out _).Should().BeFalse();
    }

    [Fact]
    public void Clear_RemovesAll() {
        using var dag = Build(("a", "b"));
        dag.Clear();
        dag.Nodes.Count.Should().Be(0);
        dag.Edges.Count.Should().Be(0);
    }

    [Fact]
    public void Version_Increments() {
        using var dag = new ConcurrentDag<string>();
        var v0 = dag.Version;
        dag.AddNode(Node("a"));
        dag.Version.Should().BeGreaterThan(v0);
    }

    // ===== 异步 API 与同步一致 =====

    [Fact]
    public async Task AddNodeAsync_MatchesSync() {
        using var dag = new ConcurrentDag<string>();
        var result = await dag.AddNodeAsync(Node("a"), CancellationToken.None);
        result.Success.Should().BeTrue();
        dag.Nodes.Count.Should().Be(1);
    }

    [Fact]
    public async Task AddEdgeAsync_MatchesSync() {
        using var dag = new ConcurrentDag<string>();
        dag.AddNode(Node("a"));
        dag.AddNode(Node("b"));
        var result = await dag.AddEdgeAsync(Edge("a", "b"), CancellationToken.None);
        result.Success.Should().BeTrue();
        dag.Edges.Count.Should().Be(1);
    }

    [Fact]
    public async Task AddEdgeAsync_Cycle_ReturnsFail() {
        using var dag = new ConcurrentDag<string>();
        dag.AddNode(Node("a"));
        dag.AddNode(Node("b"));
        await dag.AddEdgeAsync(Edge("a", "b"), CancellationToken.None);
        var result = await dag.AddEdgeAsync(Edge("b", "a"), CancellationToken.None);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task TryAddEdgeAsync_MatchesSync() {
        using var dag = new ConcurrentDag<string>();
        dag.AddNode(Node("a"));
        dag.AddNode(Node("b"));
        var result = await dag.TryAddEdgeAsync(Edge("a", "b"), CancellationToken.None);
        result.Success.Should().BeTrue();
        dag.Edges.Count.Should().Be(1);
    }

    [Fact]
    public async Task RemoveNodeAsync_MatchesSync() {
        using var dag = Build(("a", "b"), ("b", "c"));
        var result = await dag.RemoveNodeAsync("b", CancellationToken.None);
        result.Success.Should().BeTrue();
        dag.Nodes.Count.Should().Be(2);
    }

    [Fact]
    public async Task RemoveEdgeAsync_MatchesSync() {
        using var dag = Build(("a", "b"));
        var edgeId = dag.Edges.Values.First().Id;
        var result = await dag.RemoveEdgeAsync(edgeId, CancellationToken.None);
        result.Success.Should().BeTrue();
        dag.Edges.Count.Should().Be(0);
    }

    [Fact]
    public async Task WouldCreateCycleAsync_MatchesSync() {
        using var dag = Build(("a", "b"));
        (await dag.WouldCreateCycleAsync("b", "a", CancellationToken.None)).Should().BeTrue();
        (await dag.WouldCreateCycleAsync("a", "b", CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task AddNodeAsync_Cancellation_Throws() {
        using var dag = new ConcurrentDag<string>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await dag.AddNodeAsync(Node("a"), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ===== Dispose =====

    [Fact]
    public void Dispose_ThenSyncWrite_ThrowsObjectDisposed() {
        var dag = new ConcurrentDag<string>();
        dag.Dispose();
        var act = () => dag.AddNode(Node("a"));
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task Dispose_ThenAsyncWrite_ThrowsObjectDisposed() {
        var dag = new ConcurrentDag<string>();
        dag.Dispose();
        var act = async () => await dag.AddNodeAsync(Node("a"), CancellationToken.None);
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_Twice_DoesNotThrow() {
        var dag = new ConcurrentDag<string>();
        dag.Dispose();
        var act = () => dag.Dispose();
        act.Should().NotThrow();
    }

    [Fact]
    public void Nodes_AfterDispose_StillReadable() {
        var dag = Build(("a", "b"));
        dag.Dispose();
        // 无锁读 API 在 Dispose 后仍可读(inner 未释放,仅锁释放)
        dag.Nodes.Count.Should().Be(2);
        dag.Edges.Count.Should().Be(1);
        dag.Version.Should().BeGreaterThan(0);
    }

    // 注:同步 WithLock 用 _lock.Wait(0) 非阻塞尝试,单线程下锁始终空闲,
    // 无法确定性触发"锁超时返回 timeoutResult"路径。该路径需多线程竞态,
    // 违反确定性测试要求,故不覆盖。
}
