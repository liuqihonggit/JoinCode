namespace Structura.Tests;

public class ImmutableDagTests {
    private static ImmutableDagNode<string> Node(string id) => new() { Id = id, Payload = id };
    private static DagEdge Edge(string from, string to) => new() { FromId = from, ToId = to };

    [Fact]
    public void Empty_NodesAndEdgesAreEmpty() {
        var dag = ImmutableDag<string>.Empty;
        dag.Nodes.Count.Should().Be(0);
        dag.Edges.Count.Should().Be(0);
        dag.Version.Should().Be(0);
    }

    [Fact]
    public void AddNode_Single() {
        var dag = ImmutableDag<string>.Empty.AddNode(Node("a"));
        dag.Nodes.Count.Should().Be(1);
        dag.Nodes["a"].Id.Should().Be("a");
        dag.Version.Should().Be(1);
    }

    [Fact]
    public void AddNode_Duplicate_Throws() {
        var dag = ImmutableDag<string>.Empty.AddNode(Node("a"));
        var act = () => dag.AddNode(Node("a"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddEdge_MissingNode_Throws() {
        var dag = ImmutableDag<string>.Empty;
        var act = () => dag.AddEdge(Edge("a", "b"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddEdge_Single() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddEdge(Edge("a", "b"));
        dag.Edges.Count.Should().Be(1);
    }

    [Fact]
    public void AddEdge_Cycle_Throws() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddEdge(Edge("a", "b"));
        var act = () => dag.AddEdge(Edge("b", "a"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddEdge_SelfLoop_Throws() {
        var dag = ImmutableDag<string>.Empty.AddNode(Node("a"));
        var act = () => dag.AddEdge(Edge("a", "a"));
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TryAddEdge_AllowsCycle() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddEdge(Edge("a", "b"))
            .TryAddEdge(Edge("b", "a"));
        dag.Edges.Count.Should().Be(2);
    }

    [Fact]
    public void TryGetEdge_Found() {
        var edge = Edge("a", "b");
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddEdge(edge);
        dag.TryGetEdge("a", "b", out var found).Should().BeTrue();
        found!.Id.Should().Be(edge.Id);
    }

    [Fact]
    public void TryGetEdge_NotFound() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"));
        dag.TryGetEdge("a", "b", out _).Should().BeFalse();
    }

    [Fact]
    public void WouldCreateCycle_True() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddEdge(Edge("a", "b"));
        dag.WouldCreateCycle("b", "a").Should().BeTrue();
    }

    [Fact]
    public void WouldCreateCycle_False() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"));
        dag.WouldCreateCycle("a", "b").Should().BeFalse();
    }

    [Fact]
    public void WouldCreateCycle_Static() {
        var adjacency = new Dictionary<string, IReadOnlyList<string>> {
            ["a"] = new[] { "b" },
            ["b"] = new[] { "c" },
            ["c"] = Array.Empty<string>()
        };
        ImmutableDag<string>.WouldCreateCycle(adjacency, "c", "a").Should().BeTrue();
        ImmutableDag<string>.WouldCreateCycle(adjacency, "a", "c").Should().BeFalse();
    }

    [Fact]
    public void RemoveNode_RemovesAssociatedEdges() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddNode(Node("c"))
            .AddEdge(Edge("a", "b"))
            .AddEdge(Edge("b", "c"))
            .RemoveNode("b");
        dag.Nodes.Count.Should().Be(2);
        dag.Edges.Count.Should().Be(0);
    }

    [Fact]
    public void RemoveNode_NotFound_Throws() {
        var dag = ImmutableDag<string>.Empty;
        var act = () => dag.RemoveNode("x");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void RemoveEdge_PreservesNodes() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddEdge(Edge("a", "b"));
        var edgeId = dag.Edges.Values.First().Id;
        dag.RemoveEdge(edgeId);
        dag.Nodes.Count.Should().Be(2);
        dag.Edges.Count.Should().Be(0);
    }

    [Fact]
    public void TopologicalSort_Simple() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddNode(Node("c"))
            .AddEdge(Edge("a", "b"))
            .AddEdge(Edge("b", "c"));
        var sorted = dag.TopologicalSort();
        sorted.Count.Should().Be(3);
        sorted.Select(n => n.Id).Should().ContainInOrder("a", "b", "c");
    }

    [Fact]
    public void TopologicalSortByLevels_ParallelLayers() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddNode(Node("c"))
            .AddNode(Node("d"))
            .AddEdge(Edge("a", "c"))
            .AddEdge(Edge("b", "c"))
            .AddEdge(Edge("c", "d"));
        var levels = dag.TopologicalSortByLevels();
        levels.Count.Should().Be(3);
        levels[0].Select(n => n.Id).Should().BeEquivalentTo(new[] { "a", "b" });
        levels[1].Select(n => n.Id).Should().BeEquivalentTo(new[] { "c" });
        levels[2].Select(n => n.Id).Should().BeEquivalentTo(new[] { "d" });
    }

    [Fact]
    public void HasCycle_True() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddNode(Node("c"))
            .TryAddEdge(Edge("a", "b"))
            .TryAddEdge(Edge("b", "c"))
            .TryAddEdge(Edge("c", "a"));
        dag.HasCycle().Should().BeTrue();
    }

    [Fact]
    public void HasCycle_False() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddEdge(Edge("a", "b"));
        dag.HasCycle().Should().BeFalse();
    }

    [Fact]
    public void FindAllCycles_ReturnsCycle() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .TryAddEdge(Edge("a", "b"))
            .TryAddEdge(Edge("b", "a"));
        var cycles = dag.FindAllCycles();
        cycles.Should().NotBeEmpty();
    }

    [Fact]
    public void GetAncestors_ReturnsAllUpstream() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddNode(Node("c"))
            .AddEdge(Edge("a", "b"))
            .AddEdge(Edge("b", "c"));
        var ancestors = dag.GetAncestors("c").Select(n => n.Id).ToHashSet();
        ancestors.Should().BeEquivalentTo(new[] { "a", "b" });
    }

    [Fact]
    public void GetDescendants_ReturnsAllDownstream() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddNode(Node("c"))
            .AddEdge(Edge("a", "b"))
            .AddEdge(Edge("b", "c"));
        var descendants = dag.GetDescendants("a").Select(n => n.Id).ToHashSet();
        descendants.Should().BeEquivalentTo(new[] { "b", "c" });
    }

    [Fact]
    public void GetAffectedSubgraph_ReturnsTopoOrder() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddNode(Node("c"))
            .AddEdge(Edge("a", "b"))
            .AddEdge(Edge("b", "c"));
        var affected = dag.GetAffectedSubgraph("a").Select(n => n.Id).ToList();
        affected.Should().ContainInOrder("a", "b", "c");
    }

    [Fact]
    public void Clear_RemovesAll() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddEdge(Edge("a", "b"));
        dag.Clear();
        dag.Nodes.Count.Should().Be(0);
        dag.Edges.Count.Should().Be(0);
    }

    [Fact]
    public async Task AddNodeAsync_Works() {
        var dag = ImmutableDag<string>.Empty;
        await dag.AddNodeAsync(Node("a"));
        dag.Nodes.Count.Should().Be(1);
    }

    [Fact]
    public async Task AddEdgeAsync_Works() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"));
        await dag.AddEdgeAsync(Edge("a", "b"));
        dag.Edges.Count.Should().Be(1);
    }

    [Fact]
    public async Task TryAddEdgeAsync_Works() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"));
        await dag.TryAddEdgeAsync(Edge("a", "b"));
        dag.Edges.Count.Should().Be(1);
    }

    [Fact]
    public async Task RemoveNodeAsync_Works() {
        var dag = ImmutableDag<string>.Empty.AddNode(Node("a"));
        await dag.RemoveNodeAsync("a");
        dag.Nodes.Count.Should().Be(0);
    }

    [Fact]
    public async Task RemoveEdgeAsync_Works() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddEdge(Edge("a", "b"));
        var edgeId = dag.Edges.Values.First().Id;
        await dag.RemoveEdgeAsync(edgeId);
        dag.Edges.Count.Should().Be(0);
    }

    [Fact]
    public async Task WouldCreateCycleAsync_Works() {
        var dag = ImmutableDag<string>.Empty
            .AddNode(Node("a"))
            .AddNode(Node("b"))
            .AddEdge(Edge("a", "b"));
        var result = await dag.WouldCreateCycleAsync("b", "a");
        result.Should().BeTrue();
    }

    [Fact]
    public void ConcurrentAddNodes_ThreadSafe() {
        var dag = ImmutableDag<string>.Empty;
        var ids = Enumerable.Range(0, 100).Select(i => $"n{i}").ToList();
        ids.AsParallel().ForAll(id => dag.AddNode(Node(id)));
        dag.Nodes.Count.Should().Be(100);
    }

    [Fact]
    public void ConcurrentAddEdges_ThreadSafe() {
        var dag = ImmutableDag<string>.Empty;
        var ids = Enumerable.Range(0, 50).Select(i => $"n{i}").ToList();
        foreach (var id in ids) dag.AddNode(Node(id));
        var edges = ids.Zip(ids.Skip(1), (f, t) => Edge(f, t)).ToList();
        edges.AsParallel().ForAll(e => dag.AddEdge(e));
        dag.Edges.Count.Should().Be(49);
    }

    [Fact]
    public void Version_IncrementsOnMutation() {
        var dag = ImmutableDag<string>.Empty;
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

    [Fact]
    public void ImmutableDagNode_InEdgeIds_IsImmutableSet() {
        var node = new ImmutableDagNode<string> { Id = "a", Payload = "a" };
        node.InEdgeIds.Should().BeEmpty();
        node.OutEdgeIds.Should().BeEmpty();
    }
}
