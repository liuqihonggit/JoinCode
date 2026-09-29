namespace Structura.Tests;

public class DagAlgorithmsTests {
    private static Dictionary<string, int> InDegree(params (string id, int deg)[] entries) {
        var d = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (id, deg) in entries) d[id] = deg;
        return d;
    }

    private static HashSet<string> Set(params string[] ids) => new(ids, StringComparer.Ordinal);

    private static Func<string, IEnumerable<string>?> AdjFrom(params (string from, string[] tos)[] edges) {
        var map = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (from, tos) in edges) map[from] = [.. tos];
        return id => map.TryGetValue(id, out var list) ? list : null;
    }

    [Fact]
    public void KahnTraverse_Empty_ReturnsEmpty() {
        var result = DagAlgorithms.KahnTraverseSubgraph(
            new Dictionary<string, int>(StringComparer.Ordinal),
            Set(),
            _ => null);
        result.Should().BeEmpty();
    }

    [Fact]
    public void KahnTraverse_SingleNode_ReturnsIt() {
        var result = DagAlgorithms.KahnTraverseSubgraph(
            InDegree(("a", 0)),
            Set("a"),
            _ => null);
        result.Should().Equal(["a"]);
    }

    [Fact]
    public void KahnTraverse_Chain_ABC_ReturnsTopologicalOrder() {
        var result = DagAlgorithms.KahnTraverseSubgraph(
            InDegree(("a", 0), ("b", 1), ("c", 1)),
            Set("a", "b", "c"),
            AdjFrom(("a", ["b"]), ("b", ["c"])));
        result.Should().Equal(["a", "b", "c"]);
    }

    [Fact]
    public void KahnTraverse_Diamond_ABDC_PreservesTopologicalOrder() {
        var result = DagAlgorithms.KahnTraverseSubgraph(
            InDegree(("a", 0), ("b", 1), ("c", 1), ("d", 2)),
            Set("a", "b", "c", "d"),
            AdjFrom(("a", ["b", "c"]), ("b", ["d"]), ("c", ["d"])));
        result[0].Should().Be("a");
        result[3].Should().Be("d");
        result.Should().HaveCount(4);
    }

    [Fact]
    public void KahnTraverse_TwoRoots_BothAppearFirst() {
        var result = DagAlgorithms.KahnTraverseSubgraph(
            InDegree(("a", 0), ("b", 0), ("c", 2)),
            Set("a", "b", "c"),
            AdjFrom(("a", ["c"]), ("b", ["c"])));
        result[2].Should().Be("c");
        result.Should().Contain("a").And.Contain("b");
    }

    [Fact]
    public void KahnTraverse_DescendantsFilter_ExcludesNonDescendants() {
        var result = DagAlgorithms.KahnTraverseSubgraph(
            InDegree(("a", 0), ("b", 1)),
            Set("a", "b"),
            AdjFrom(("a", ["b"]), ("x", ["b"])));
        result.Should().Equal(["a", "b"]);
    }

    [Fact]
    public void KahnTraverse_NullAdjacent_HandlesGracefully() {
        var result = DagAlgorithms.KahnTraverseSubgraph(
            InDegree(("a", 0), ("b", 0)),
            Set("a", "b"),
            _ => null);
        result.Should().HaveCount(2);
        result.Should().Contain("a").And.Contain("b");
    }

    [Fact]
    public void KahnTraverse_SelfLoop_NotPossibleInDag_ButDoesNotInfiniteLoop() {
        var inDegree = InDegree(("a", 1));
        var result = DagAlgorithms.KahnTraverseSubgraph(
            inDegree,
            Set("a"),
            AdjFrom(("a", ["a"])));
        result.Should().BeEmpty();
    }
}
