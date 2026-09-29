namespace Structura.Tests;

public class ImmutableDagRemoveOneIncidentEdgeTests {
    private static ImmutableHamT<string, DagEdge> EmptyEdges() =>
        ImmutableHamT.Create<string, DagEdge>(StringComparer.Ordinal);
    private static ImmutableHamT<(string, string), string> EmptyEdgeIndex() =>
        ImmutableHamT.Create<(string, string), string>();
    private static ImmutableHamT<string, ImmutableDagNode<string>> EmptyNodes() =>
        ImmutableHamT.Create<string, ImmutableDagNode<string>>(StringComparer.Ordinal);
    private static ImmutableHamT<string, ImmutableHamTSet<string>> EmptyAdj() =>
        ImmutableHamT.Create<string, ImmutableHamTSet<string>>(StringComparer.Ordinal);

    private static (ImmutableHamT<string, DagEdge> edges,
                    ImmutableHamT<(string, string), string> edgeIndex,
                    ImmutableHamT<string, ImmutableDagNode<string>> nodes,
                    ImmutableHamT<string, ImmutableHamTSet<string>> adj,
                    ImmutableHamT<string, ImmutableHamTSet<string>> revAdj)
        BuildGraphWithEdgeAB() {
        var edge = new DagEdge { FromId = "a", ToId = "b", Id = "e1" };
        var edges = EmptyEdges().SetItem("e1", edge);
        var edgeIndex = EmptyEdgeIndex().SetItem(("a", "b"), "e1");
        var nodeA = new ImmutableDagNode<string> { Id = "a", Payload = "a", OutEdgeIds = ImmutableHamTSet<string>.Empty.Add("e1") };
        var nodeB = new ImmutableDagNode<string> { Id = "b", Payload = "b", InEdgeIds = ImmutableHamTSet<string>.Empty.Add("e1") };
        var nodes = EmptyNodes().SetItem("a", nodeA).SetItem("b", nodeB);
        var adj = EmptyAdj().SetItem("a", ImmutableHamTSet<string>.Empty.Add("b")).SetItem("b", ImmutableHamTSet<string>.Empty);
        var revAdj = EmptyAdj().SetItem("b", ImmutableHamTSet<string>.Empty.Add("a")).SetItem("a", ImmutableHamTSet<string>.Empty);
        return (edges, edgeIndex, nodes, adj, revAdj);
    }

    [Fact]
    public void RemoveOneIncidentEdge_RemovesEdgeFromEdges() {
        var (edges, edgeIndex, nodes, adj, revAdj) = BuildGraphWithEdgeAB();
        ImmutableDag<string>.RemoveOneIncidentEdge(ref edges, ref edgeIndex, ref nodes, ref adj, ref revAdj, "e1");
        edges.ContainsKey("e1").Should().BeFalse();
    }

    [Fact]
    public void RemoveOneIncidentEdge_RemovesEdgeFromEdgeIndex() {
        var (edges, edgeIndex, nodes, adj, revAdj) = BuildGraphWithEdgeAB();
        ImmutableDag<string>.RemoveOneIncidentEdge(ref edges, ref edgeIndex, ref nodes, ref adj, ref revAdj, "e1");
        edgeIndex.ContainsKey(("a", "b")).Should().BeFalse();
    }

    [Fact]
    public void RemoveOneIncidentEdge_RemovesFromSourceOutEdges() {
        var (edges, edgeIndex, nodes, adj, revAdj) = BuildGraphWithEdgeAB();
        ImmutableDag<string>.RemoveOneIncidentEdge(ref edges, ref edgeIndex, ref nodes, ref adj, ref revAdj, "e1");
        nodes["a"].OutEdgeIds.Contains("e1").Should().BeFalse();
    }

    [Fact]
    public void RemoveOneIncidentEdge_RemovesFromTargetInEdges() {
        var (edges, edgeIndex, nodes, adj, revAdj) = BuildGraphWithEdgeAB();
        ImmutableDag<string>.RemoveOneIncidentEdge(ref edges, ref edgeIndex, ref nodes, ref adj, ref revAdj, "e1");
        nodes["b"].InEdgeIds.Contains("e1").Should().BeFalse();
    }

    [Fact]
    public void RemoveOneIncidentEdge_RemovesFromAdjacency() {
        var (edges, edgeIndex, nodes, adj, revAdj) = BuildGraphWithEdgeAB();
        ImmutableDag<string>.RemoveOneIncidentEdge(ref edges, ref edgeIndex, ref nodes, ref adj, ref revAdj, "e1");
        adj["a"].Contains("b").Should().BeFalse();
    }

    [Fact]
    public void RemoveOneIncidentEdge_RemovesFromReverseAdjacency() {
        var (edges, edgeIndex, nodes, adj, revAdj) = BuildGraphWithEdgeAB();
        ImmutableDag<string>.RemoveOneIncidentEdge(ref edges, ref edgeIndex, ref nodes, ref adj, ref revAdj, "e1");
        revAdj["b"].Contains("a").Should().BeFalse();
    }

    [Fact]
    public void RemoveOneIncidentEdge_NonExistentEdge_NoChange() {
        var (edges, edgeIndex, nodes, adj, revAdj) = BuildGraphWithEdgeAB();
        var edgesBefore = edges;
        ImmutableDag<string>.RemoveOneIncidentEdge(ref edges, ref edgeIndex, ref nodes, ref adj, ref revAdj, "nonexistent");
        edges.Should().BeSameAs(edgesBefore);
    }

    [Fact]
    public void RemoveOneIncidentEdge_PreservesNodePayload() {
        var (edges, edgeIndex, nodes, adj, revAdj) = BuildGraphWithEdgeAB();
        ImmutableDag<string>.RemoveOneIncidentEdge(ref edges, ref edgeIndex, ref nodes, ref adj, ref revAdj, "e1");
        nodes["a"].Payload.Should().Be("a");
        nodes["b"].Payload.Should().Be("b");
    }
}
