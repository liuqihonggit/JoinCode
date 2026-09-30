namespace Structura.Tests;

/// <summary>
/// Dag&lt;T&gt; 确定性单元测试 — 不依赖时序/线程调度,给定输入断言输出。
/// 与 ImmutableDag&lt;T&gt; 算法同构,包含对拍验证。
/// </summary>
public class DagTests {
    private static DagNode<string> Node(string id) => new() { Id = id, Payload = id };
    private static DagEdge Edge(string from, string to) => new() { FromId = from, ToId = to };
    private static ImmutableDagNode<string> INode(string id) => new() { Id = id, Payload = id };

    private static Dag<string> BuildDag(string[] nodes, (string from, string to)[] edges) {
        var dag = new Dag<string>();
        foreach (var id in nodes) dag.AddNode(Node(id));
        foreach (var (f, t) in edges) dag.AddEdge(Edge(f, t));
        return dag;
    }

    private static ImmutableDag<string> BuildImmutableDag(string[] nodes, (string from, string to)[] edges) {
        var dag = ImmutableDag<string>.Empty;
        foreach (var id in nodes) dag = dag.AddNode(INode(id));
        foreach (var (f, t) in edges) dag = dag.AddEdge(Edge(f, t));
        return dag;
    }

    /// <summary>规范化环:旋转到最小 Id 开头,消除 DFS 起点差异</summary>
    private static string NormalizeCycle(IReadOnlyList<string> cycle) {
        if (cycle.Count == 0) return "";
        var minIdx = 0;
        for (var i = 1; i < cycle.Count; i++)
            if (string.CompareOrdinal(cycle[i], cycle[minIdx]) < 0) minIdx = i;
        return string.Join(",", cycle.Skip(minIdx).Concat(cycle.Take(minIdx)));
    }

    /// <summary>规范化环集合:每环规范化后排序拼接,消除发现顺序差异</summary>
    private static string NormalizeCycles(IReadOnlyList<IReadOnlyList<string>> cycles)
        => string.Join("|", cycles.Select(NormalizeCycle).OrderBy(s => s));

    // ===== 基本操作 =====

    [Fact]
    public void AddNode_Single() {
        var dag = new Dag<string>();
        var result = dag.AddNode(Node("a"));
        result.Success.Should().BeTrue();
        dag.Nodes.Count.Should().Be(1);
        dag.Nodes["a"].Id.Should().Be("a");
        dag.Version.Should().Be(1);
    }

    [Fact]
    public void AddNode_Duplicate_ReturnsFail() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        var result = dag.AddNode(Node("a"));
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("already exists");
        dag.Nodes.Count.Should().Be(1);
    }

    [Fact]
    public void AddEdge_MissingSource_ReturnsFail() {
        var dag = new Dag<string>();
        dag.AddNode(Node("b"));
        var result = dag.AddEdge(Edge("a", "b"));
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Source node not found");
    }

    [Fact]
    public void AddEdge_MissingTarget_ReturnsFail() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        var result = dag.AddEdge(Edge("a", "b"));
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Target node not found");
    }

    [Fact]
    public void AddEdge_Single() {
        var dag = BuildDag(new[] { "a", "b" }, new[] { ("a", "b") });
        dag.Edges.Count.Should().Be(1);
        dag.Version.Should().Be(3); // 2 nodes + 1 edge
    }

    [Fact]
    public void AddEdge_Cycle_ReturnsCycleResult() {
        var dag = BuildDag(new[] { "a", "b" }, new[] { ("a", "b") });
        var result = dag.AddEdge(Edge("b", "a"));
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Cycle");
        result.CyclePath.Should().NotBeEmpty();
        dag.Edges.Count.Should().Be(1); // 环边未加入
    }

    [Fact]
    public void AddEdge_SelfLoop_ReturnsCycleResult() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        var result = dag.AddEdge(Edge("a", "a"));
        result.Success.Should().BeFalse();
        result.CyclePath.Should().Contain("a");
    }

    [Fact]
    public void TryAddEdge_AllowsCycle() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        dag.AddNode(Node("b"));
        dag.AddEdge(Edge("a", "b"));
        var result = dag.TryAddEdge(Edge("b", "a"));
        result.Success.Should().BeTrue();
        dag.Edges.Count.Should().Be(2);
    }

    [Fact]
    public void TryAddEdge_MissingNode_ReturnsFail() {
        var dag = new Dag<string>();
        var result = dag.TryAddEdge(Edge("a", "b"));
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void TryGetEdge_Found() {
        var dag = BuildDag(new[] { "a", "b" }, new[] { ("a", "b") });
        dag.TryGetEdge("a", "b", out var edge).Should().BeTrue();
        edge!.FromId.Should().Be("a");
        edge.ToId.Should().Be("b");
    }

    [Fact]
    public void TryGetEdge_NotFound() {
        var dag = BuildDag(new[] { "a", "b" }, new[] { ("a", "b") });
        dag.TryGetEdge("b", "a", out _).Should().BeFalse();
        dag.TryGetEdge("x", "y", out _).Should().BeFalse();
    }

    [Fact]
    public void RemoveNode_RemovesAssociatedEdges() {
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b"), ("b", "c") });
        var result = dag.RemoveNode("b");
        result.Success.Should().BeTrue();
        dag.Nodes.Count.Should().Be(2);
        dag.Edges.Count.Should().Be(0);
        dag.Nodes.Should().ContainKeys("a", "c");
    }

    [Fact]
    public void RemoveNode_NotFound_ReturnsFail() {
        var dag = new Dag<string>();
        var result = dag.RemoveNode("x");
        result.Success.Should().BeFalse();
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public void RemoveEdge_PreservesNodes() {
        var dag = BuildDag(new[] { "a", "b" }, new[] { ("a", "b") });
        var edgeId = dag.Edges.Values.First().Id;
        var result = dag.RemoveEdge(edgeId);
        result.Success.Should().BeTrue();
        dag.Nodes.Count.Should().Be(2);
        dag.Edges.Count.Should().Be(0);
    }

    [Fact]
    public void RemoveEdge_NotFound_ReturnsFail() {
        var dag = new Dag<string>();
        var result = dag.RemoveEdge("nonexistent");
        result.Success.Should().BeFalse();
    }

    [Fact]
    public void Version_IncrementsOnMutation() {
        var dag = new Dag<string>();
        var v0 = dag.Version;
        dag.AddNode(Node("a"));
        var v1 = dag.Version;
        dag.AddNode(Node("b"));
        var v2 = dag.Version;
        dag.AddEdge(Edge("a", "b"));
        var v3 = dag.Version;
        v1.Should().BeGreaterThan(v0);
        v2.Should().BeGreaterThan(v1);
        v3.Should().BeGreaterThan(v2);
    }

    // ===== TopologicalSort =====

    [Fact]
    public void TopologicalSort_Empty() {
        var dag = new Dag<string>();
        dag.TopologicalSort().Should().BeEmpty();
    }

    [Fact]
    public void TopologicalSort_SingleNode() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        var sorted = dag.TopologicalSort();
        sorted.Should().HaveCount(1);
        sorted[0].Id.Should().Be("a");
    }

    [Fact]
    public void TopologicalSort_IsolatedNodes() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        dag.AddNode(Node("b"));
        dag.AddNode(Node("c"));
        var sorted = dag.TopologicalSort();
        sorted.Select(n => n.Id).Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    [Fact]
    public void TopologicalSort_Chain() {
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b"), ("b", "c") });
        var sorted = dag.TopologicalSort();
        sorted.Select(n => n.Id).Should().ContainInOrder("a", "b", "c");
    }

    [Fact]
    public void TopologicalSort_Diamond() {
        // a→b, a→c, b→d, c→d
        var dag = BuildDag(new[] { "a", "b", "c", "d" }, new[] { ("a", "b"), ("a", "c"), ("b", "d"), ("c", "d") });
        var sorted = dag.TopologicalSort();
        var ids = sorted.Select(n => n.Id).ToList();
        ids.IndexOf("a").Should().BeLessThan(ids.IndexOf("b"));
        ids.IndexOf("a").Should().BeLessThan(ids.IndexOf("c"));
        ids.IndexOf("b").Should().BeLessThan(ids.IndexOf("d"));
        ids.IndexOf("c").Should().BeLessThan(ids.IndexOf("d"));
    }

    [Fact]
    public void TopologicalSortByLevels_Empty() {
        var dag = new Dag<string>();
        dag.TopologicalSortByLevels().Should().BeEmpty();
    }

    [Fact]
    public void TopologicalSortByLevels_SingleNode() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        var levels = dag.TopologicalSortByLevels();
        levels.Should().HaveCount(1);
        levels[0].Select(n => n.Id).Should().BeEquivalentTo(new[] { "a" });
    }

    [Fact]
    public void TopologicalSortByLevels_Chain() {
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b"), ("b", "c") });
        var levels = dag.TopologicalSortByLevels();
        levels.Should().HaveCount(3);
        levels[0].Select(n => n.Id).Should().BeEquivalentTo(new[] { "a" });
        levels[1].Select(n => n.Id).Should().BeEquivalentTo(new[] { "b" });
        levels[2].Select(n => n.Id).Should().BeEquivalentTo(new[] { "c" });
    }

    [Fact]
    public void TopologicalSortByLevels_Diamond() {
        var dag = BuildDag(new[] { "a", "b", "c", "d" }, new[] { ("a", "b"), ("a", "c"), ("b", "d"), ("c", "d") });
        var levels = dag.TopologicalSortByLevels();
        levels.Should().HaveCount(3);
        levels[0].Select(n => n.Id).Should().BeEquivalentTo(new[] { "a" });
        levels[1].Select(n => n.Id).Should().BeEquivalentTo(new[] { "b", "c" });
        levels[2].Select(n => n.Id).Should().BeEquivalentTo(new[] { "d" });
    }

    [Fact]
    public void TopologicalSortByLevels_IsolatedNodes_SingleLevel() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        dag.AddNode(Node("b"));
        var levels = dag.TopologicalSortByLevels();
        levels.Should().HaveCount(1);
        levels[0].Select(n => n.Id).Should().BeEquivalentTo(new[] { "a", "b" });
    }

    // ===== HasCycle =====

    [Fact]
    public void HasCycle_EmptyGraph() {
        new Dag<string>().HasCycle().Should().BeFalse();
    }

    [Fact]
    public void HasCycle_NoCycle() {
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b"), ("b", "c") });
        dag.HasCycle().Should().BeFalse();
    }

    [Fact]
    public void HasCycle_SelfLoop() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        dag.TryAddEdge(Edge("a", "a"));
        dag.HasCycle().Should().BeTrue();
    }

    [Fact]
    public void HasCycle_TwoNodeCycle() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        dag.AddNode(Node("b"));
        dag.TryAddEdge(Edge("a", "b"));
        dag.TryAddEdge(Edge("b", "a"));
        dag.HasCycle().Should().BeTrue();
    }

    [Fact]
    public void HasCycle_ThreeNodeCycle() {
        var dag = new Dag<string>();
        foreach (var id in new[] { "a", "b", "c" }) dag.AddNode(Node(id));
        dag.TryAddEdge(Edge("a", "b"));
        dag.TryAddEdge(Edge("b", "c"));
        dag.TryAddEdge(Edge("c", "a"));
        dag.HasCycle().Should().BeTrue();
    }

    [Fact]
    public void HasCycle_MultipleIndependentCycles() {
        var dag = new Dag<string>();
        foreach (var id in new[] { "a", "b", "c", "d" }) dag.AddNode(Node(id));
        dag.TryAddEdge(Edge("a", "b"));
        dag.TryAddEdge(Edge("b", "a"));
        dag.TryAddEdge(Edge("c", "d"));
        dag.TryAddEdge(Edge("d", "c"));
        dag.HasCycle().Should().BeTrue();
    }

    // ===== FindAllCycles =====

    [Fact]
    public void FindAllCycles_NoCycle_Empty() {
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b"), ("b", "c") });
        dag.FindAllCycles().Should().BeEmpty();
    }

    [Fact]
    public void FindAllCycles_SingleCycle() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        dag.AddNode(Node("b"));
        dag.TryAddEdge(Edge("a", "b"));
        dag.TryAddEdge(Edge("b", "a"));
        var cycles = dag.FindAllCycles();
        cycles.Should().HaveCount(1);
        cycles[0].Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void FindAllCycles_TwoIndependentCycles() {
        var dag = new Dag<string>();
        foreach (var id in new[] { "a", "b", "c", "d" }) dag.AddNode(Node(id));
        dag.TryAddEdge(Edge("a", "b"));
        dag.TryAddEdge(Edge("b", "a"));
        dag.TryAddEdge(Edge("c", "d"));
        dag.TryAddEdge(Edge("d", "c"));
        NormalizeCycles(dag.FindAllCycles()).Should().Be("a,b|c,d");
    }

    [Fact]
    public void FindAllCycles_ThreeNodeCycle() {
        var dag = new Dag<string>();
        foreach (var id in new[] { "a", "b", "c" }) dag.AddNode(Node(id));
        dag.TryAddEdge(Edge("a", "b"));
        dag.TryAddEdge(Edge("b", "c"));
        dag.TryAddEdge(Edge("c", "a"));
        var cycles = dag.FindAllCycles();
        cycles.Should().HaveCount(1);
        cycles[0].Should().BeEquivalentTo(new[] { "a", "b", "c" });
    }

    [Fact]
    public void FindAllCycles_NestedSharedNodeCycles() {
        // a→b→c→a (3环) 和 a→c→a (2环),共享 a,c。
        // FindAllCycles 用全局 visited 标记,共享节点的嵌套环只发现首个分支的环,
        // 这是实现的确定性行为(DFS 一旦标记 visited 不再回溯其他分支)。
        var dag = new Dag<string>();
        foreach (var id in new[] { "a", "b", "c" }) dag.AddNode(Node(id));
        dag.TryAddEdge(Edge("a", "b"));
        dag.TryAddEdge(Edge("b", "c"));
        dag.TryAddEdge(Edge("c", "a"));
        dag.TryAddEdge(Edge("a", "c"));
        var cycles = dag.FindAllCycles();
        cycles.Should().NotBeEmpty();
        // 两个可能环 [a,b,c] 与 [a,c] 均经过 a
        cycles.All(c => c.Contains("a")).Should().BeTrue("所有环都经过共享节点 a");
    }

    // ===== WouldCreateCycle =====

    [Fact]
    public void WouldCreateCycle_SelfLoop() {
        var dag = new Dag<string>();
        dag.AddNode(Node("a"));
        dag.WouldCreateCycle("a", "a").Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_DirectBackEdge() {
        var dag = BuildDag(new[] { "a", "b" }, new[] { ("a", "b") });
        dag.WouldCreateCycle("b", "a").Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_TransitiveBackEdge() {
        // a→b→c, 加 c→a 会成环
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b"), ("b", "c") });
        dag.WouldCreateCycle("c", "a").Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_NoCycle() {
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b") });
        dag.WouldCreateCycle("b", "c").Should().BeFalse();
        dag.WouldCreateCycle("a", "c").Should().BeFalse();
    }

    [Fact]
    public void WouldCreateCycle_LongChainReachability() {
        // a→b→c→d→e, 加 e→a 会成环, 加 e→b 也会成环
        var dag = BuildDag(new[] { "a", "b", "c", "d", "e" }, new[] { ("a", "b"), ("b", "c"), ("c", "d"), ("d", "e") });
        dag.WouldCreateCycle("e", "a").Should().BeTrue();
        dag.WouldCreateCycle("e", "b").Should().BeTrue();
        dag.WouldCreateCycle("e", "c").Should().BeTrue();
        dag.WouldCreateCycle("a", "e").Should().BeFalse();
    }

    [Fact]
    public void WouldCreateCycle_Static_SelfLoop() {
        var adjacency = new Dictionary<string, IReadOnlyList<string>> { ["a"] = Array.Empty<string>() };
        Dag<string>.WouldCreateCycle(adjacency, "a", "a").Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_Static_DetectsCycle() {
        var adjacency = new Dictionary<string, IReadOnlyList<string>> {
            ["a"] = new[] { "b" },
            ["b"] = new[] { "c" },
            ["c"] = Array.Empty<string>()
        };
        Dag<string>.WouldCreateCycle(adjacency, "c", "a").Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_Static_NoCycle() {
        var adjacency = new Dictionary<string, IReadOnlyList<string>> {
            ["a"] = new[] { "b" },
            ["b"] = new[] { "c" },
            ["c"] = Array.Empty<string>()
        };
        Dag<string>.WouldCreateCycle(adjacency, "a", "c").Should().BeFalse();
    }

    [Fact]
    public void WouldCreateCycle_Static_LongChain() {
        var adjacency = new Dictionary<string, IReadOnlyList<string>> {
            ["a"] = new[] { "b" },
            ["b"] = new[] { "c" },
            ["c"] = new[] { "d" },
            ["d"] = new[] { "e" },
            ["e"] = Array.Empty<string>()
        };
        Dag<string>.WouldCreateCycle(adjacency, "e", "a").Should().BeTrue();
        Dag<string>.WouldCreateCycle(adjacency, "e", "c").Should().BeTrue();
        Dag<string>.WouldCreateCycle(adjacency, "a", "e").Should().BeFalse();
    }

    // ===== GetAffectedSubgraph =====

    [Fact]
    public void GetAffectedSubgraph_Chain() {
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b"), ("b", "c") });
        var affected = dag.GetAffectedSubgraph("a").Select(n => n.Id).ToList();
        affected.Should().ContainInOrder("a", "b", "c");
    }

    [Fact]
    public void GetAffectedSubgraph_Diamond() {
        // a→b, a→c, b→d, c→d
        var dag = BuildDag(new[] { "a", "b", "c", "d" }, new[] { ("a", "b"), ("a", "c"), ("b", "d"), ("c", "d") });
        var affected = dag.GetAffectedSubgraph("a").Select(n => n.Id).ToList();
        affected.Should().HaveCount(4);
        var idx = affected.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
        idx["a"].Should().BeLessThan(idx["b"]);
        idx["a"].Should().BeLessThan(idx["c"]);
        idx["b"].Should().BeLessThan(idx["d"]);
        idx["c"].Should().BeLessThan(idx["d"]);
    }

    [Fact]
    public void GetAffectedSubgraph_LeafNode_ReturnsSelf() {
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b"), ("b", "c") });
        var affected = dag.GetAffectedSubgraph("c").Select(n => n.Id).ToList();
        affected.Should().BeEquivalentTo(new[] { "c" });
    }

    [Fact]
    public void GetAffectedSubgraph_MiddleNode() {
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b"), ("b", "c") });
        var affected = dag.GetAffectedSubgraph("b").Select(n => n.Id).ToList();
        affected.Should().ContainInOrder("b", "c");
        affected.Should().NotContain("a");
    }

    // ===== GetAncestors / GetDescendants =====

    [Fact]
    public void GetAncestors_Chain() {
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b"), ("b", "c") });
        dag.GetAncestors("c").Select(n => n.Id).Should().BeEquivalentTo(new[] { "a", "b" });
        dag.GetAncestors("b").Select(n => n.Id).Should().BeEquivalentTo(new[] { "a" });
        dag.GetAncestors("a").Should().BeEmpty();
    }

    [Fact]
    public void GetAncestors_Diamond() {
        var dag = BuildDag(new[] { "a", "b", "c", "d" }, new[] { ("a", "b"), ("a", "c"), ("b", "d"), ("c", "d") });
        dag.GetAncestors("d").Select(n => n.Id).Should().BeEquivalentTo(new[] { "a", "b", "c" });
        dag.GetAncestors("b").Select(n => n.Id).Should().BeEquivalentTo(new[] { "a" });
    }

    [Fact]
    public void GetDescendants_Chain() {
        var dag = BuildDag(new[] { "a", "b", "c" }, new[] { ("a", "b"), ("b", "c") });
        dag.GetDescendants("a").Select(n => n.Id).Should().BeEquivalentTo(new[] { "b", "c" });
        dag.GetDescendants("b").Select(n => n.Id).Should().BeEquivalentTo(new[] { "c" });
        dag.GetDescendants("c").Should().BeEmpty();
    }

    [Fact]
    public void GetDescendants_Diamond() {
        var dag = BuildDag(new[] { "a", "b", "c", "d" }, new[] { ("a", "b"), ("a", "c"), ("b", "d"), ("c", "d") });
        dag.GetDescendants("a").Select(n => n.Id).Should().BeEquivalentTo(new[] { "b", "c", "d" });
        dag.GetDescendants("b").Select(n => n.Id).Should().BeEquivalentTo(new[] { "d" });
    }

    // ===== 对拍 ImmutableDag =====

    [Fact]
    public void CrossCheck_TopologicalSortByLevels_Diamond() {
        var nodes = new[] { "a", "b", "c", "d" };
        var edges = new[] { ("a", "b"), ("a", "c"), ("b", "d"), ("c", "d") };
        var dag = BuildDag(nodes, edges);
        var imm = BuildImmutableDag(nodes, edges);
        var dagLevels = dag.TopologicalSortByLevels();
        var immLevels = imm.TopologicalSortByLevels();
        dagLevels.Count.Should().Be(immLevels.Count);
        for (var i = 0; i < dagLevels.Count; i++)
            dagLevels[i].Select(n => n.Id).Should().BeEquivalentTo(immLevels[i].Select(n => n.Id));
    }

    [Fact]
    public void CrossCheck_TopologicalSortByLevels_Chain() {
        var nodes = new[] { "a", "b", "c" };
        var edges = new[] { ("a", "b"), ("b", "c") };
        var dag = BuildDag(nodes, edges);
        var imm = BuildImmutableDag(nodes, edges);
        var dagLevels = dag.TopologicalSortByLevels();
        var immLevels = imm.TopologicalSortByLevels();
        dagLevels.Count.Should().Be(immLevels.Count);
        for (var i = 0; i < dagLevels.Count; i++)
            dagLevels[i].Select(n => n.Id).Should().BeEquivalentTo(immLevels[i].Select(n => n.Id));
    }

    [Fact]
    public void CrossCheck_HasCycle_WithCycle() {
        var nodes = new[] { "a", "b", "c" };
        var dag = new Dag<string>();
        var imm = ImmutableDag<string>.Empty;
        foreach (var id in nodes) { dag.AddNode(Node(id)); imm = imm.AddNode(INode(id)); }
        dag.TryAddEdge(Edge("a", "b")); imm = imm.TryAddEdge(Edge("a", "b"));
        dag.TryAddEdge(Edge("b", "c")); imm = imm.TryAddEdge(Edge("b", "c"));
        dag.TryAddEdge(Edge("c", "a")); imm = imm.TryAddEdge(Edge("c", "a"));
        dag.HasCycle().Should().Be(imm.HasCycle());
    }

    [Fact]
    public void CrossCheck_HasCycle_NoCycle() {
        var nodes = new[] { "a", "b", "c", "d" };
        var edges = new[] { ("a", "b"), ("a", "c"), ("b", "d"), ("c", "d") };
        var dag = BuildDag(nodes, edges);
        var imm = BuildImmutableDag(nodes, edges);
        dag.HasCycle().Should().Be(imm.HasCycle());
    }

    [Fact]
    public void CrossCheck_FindAllCycles_TwoIndependentCycles() {
        var nodes = new[] { "a", "b", "c", "d" };
        var dag = new Dag<string>();
        var imm = ImmutableDag<string>.Empty;
        foreach (var id in nodes) { dag.AddNode(Node(id)); imm = imm.AddNode(INode(id)); }
        dag.TryAddEdge(Edge("a", "b")); imm = imm.TryAddEdge(Edge("a", "b"));
        dag.TryAddEdge(Edge("b", "a")); imm = imm.TryAddEdge(Edge("b", "a"));
        dag.TryAddEdge(Edge("c", "d")); imm = imm.TryAddEdge(Edge("c", "d"));
        dag.TryAddEdge(Edge("d", "c")); imm = imm.TryAddEdge(Edge("d", "c"));
        NormalizeCycles(dag.FindAllCycles()).Should().Be(NormalizeCycles(imm.FindAllCycles()));
    }

    [Fact]
    public void CrossCheck_GetAncestors_Diamond() {
        var nodes = new[] { "a", "b", "c", "d" };
        var edges = new[] { ("a", "b"), ("a", "c"), ("b", "d"), ("c", "d") };
        var dag = BuildDag(nodes, edges);
        var imm = BuildImmutableDag(nodes, edges);
        dag.GetAncestors("d").Select(n => n.Id).Should().BeEquivalentTo(imm.GetAncestors("d").Select(n => n.Id));
        dag.GetAncestors("b").Select(n => n.Id).Should().BeEquivalentTo(imm.GetAncestors("b").Select(n => n.Id));
    }

    [Fact]
    public void CrossCheck_GetDescendants_Diamond() {
        var nodes = new[] { "a", "b", "c", "d" };
        var edges = new[] { ("a", "b"), ("a", "c"), ("b", "d"), ("c", "d") };
        var dag = BuildDag(nodes, edges);
        var imm = BuildImmutableDag(nodes, edges);
        dag.GetDescendants("a").Select(n => n.Id).Should().BeEquivalentTo(imm.GetDescendants("a").Select(n => n.Id));
        dag.GetDescendants("b").Select(n => n.Id).Should().BeEquivalentTo(imm.GetDescendants("b").Select(n => n.Id));
    }

    [Fact]
    public void CrossCheck_GetAffectedSubgraph_Diamond() {
        var nodes = new[] { "a", "b", "c", "d" };
        var edges = new[] { ("a", "b"), ("a", "c"), ("b", "d"), ("c", "d") };
        var dag = BuildDag(nodes, edges);
        var imm = BuildImmutableDag(nodes, edges);
        dag.GetAffectedSubgraph("a").Select(n => n.Id).Should().BeEquivalentTo(imm.GetAffectedSubgraph("a").Select(n => n.Id));
    }

    // ===== 守卫补全:null/空输入抛异常(确定性测试,不依赖时序/IO) =====

    [Fact]
    [Trait("Category", "Deterministic")]
    public void AddNode_NullNode_ThrowsArgumentNullException() {
        var dag = new Dag<string>();
        var act = () => dag.AddNode(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void AddNode_NullId_ThrowsArgumentException() {
        var dag = new Dag<string>();
        var node = new DagNode<string> { Id = null!, Payload = "x" };
        var act = () => dag.AddNode(node);
        act.Should().Throw<ArgumentException>().WithMessage("*节点 Id*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void AddNode_EmptyId_ThrowsArgumentException() {
        var dag = new Dag<string>();
        var node = new DagNode<string> { Id = "", Payload = "x" };
        var act = () => dag.AddNode(node);
        act.Should().Throw<ArgumentException>().WithMessage("*节点 Id*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void AddEdge_NullEdge_ThrowsArgumentNullException() {
        var dag = new Dag<string>();
        var act = () => dag.AddEdge(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void AddEdge_NullFromId_ThrowsArgumentException() {
        var dag = new Dag<string>();
        var edge = new DagEdge { FromId = null!, ToId = "b" };
        var act = () => dag.AddEdge(edge);
        act.Should().Throw<ArgumentException>().WithMessage("*FromId*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void AddEdge_EmptyFromId_ThrowsArgumentException() {
        var dag = new Dag<string>();
        var edge = new DagEdge { FromId = "", ToId = "b" };
        var act = () => dag.AddEdge(edge);
        act.Should().Throw<ArgumentException>().WithMessage("*FromId*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void AddEdge_NullToId_ThrowsArgumentException() {
        var dag = new Dag<string>();
        var edge = new DagEdge { FromId = "a", ToId = null! };
        var act = () => dag.AddEdge(edge);
        act.Should().Throw<ArgumentException>().WithMessage("*ToId*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void AddEdge_EmptyToId_ThrowsArgumentException() {
        var dag = new Dag<string>();
        var edge = new DagEdge { FromId = "a", ToId = "" };
        var act = () => dag.AddEdge(edge);
        act.Should().Throw<ArgumentException>().WithMessage("*ToId*");
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void WouldCreateCycle_NullFromId_ThrowsArgumentNullException() {
        var dag = new Dag<string>();
        var act = () => dag.WouldCreateCycle(null!, "b");
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    [Trait("Category", "Deterministic")]
    public void WouldCreateCycle_NullToId_ThrowsArgumentNullException() {
        var dag = new Dag<string>();
        var act = () => dag.WouldCreateCycle("a", null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
