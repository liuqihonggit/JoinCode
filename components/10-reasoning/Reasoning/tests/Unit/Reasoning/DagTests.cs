namespace JoinCode.Reasoning.Tests;

using Infrastructure.Dag;

public sealed class DagTests
{
    [Fact]
    public void AddNode_ShouldSucceed()
    {
        var dag = new Dag<string>();
        var result = dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        Assert.True(result.Success);
        Assert.Single(dag.Nodes);
    }

    [Fact]
    public void AddNode_Duplicate_ShouldFail()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        var result = dag.AddNode(new DagNode<string> { Id = "a", Payload = "A2" });
        Assert.False(result.Success);
    }

    [Fact]
    public void AddEdge_ShouldLinkNodes()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        dag.AddNode(new DagNode<string> { Id = "b", Payload = "B" });
        var result = dag.AddEdge(new DagEdge { FromId = "a", ToId = "b", Label = "SUPPORTS" });

        Assert.True(result.Success);
        Assert.Single(dag.Edges);
        Assert.Contains("b", dag.Nodes["a"].OutEdgeIds.Select(id => dag.Edges[id].ToId));
    }

    [Fact]
    public void AddEdge_SelfLoop_ShouldFail()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        var result = dag.AddEdge(new DagEdge { FromId = "a", ToId = "a" });
        Assert.False(result.Success);
    }

    [Fact]
    public void AddEdge_Cycle_ShouldFail()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        dag.AddNode(new DagNode<string> { Id = "b", Payload = "B" });
        dag.AddNode(new DagNode<string> { Id = "c", Payload = "C" });

        dag.AddEdge(new DagEdge { FromId = "a", ToId = "b" });
        dag.AddEdge(new DagEdge { FromId = "b", ToId = "c" });

        var result = dag.AddEdge(new DagEdge { FromId = "c", ToId = "a" });
        Assert.False(result.Success);
        Assert.NotNull(result.CyclePath);
        Assert.True(result.CyclePath.Count >= 2);
    }

    [Fact]
    public void HasCycle_NoCycle_ShouldReturnFalse()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        dag.AddNode(new DagNode<string> { Id = "b", Payload = "B" });
        dag.AddNode(new DagNode<string> { Id = "c", Payload = "C" });
        dag.AddEdge(new DagEdge { FromId = "a", ToId = "b" });
        dag.AddEdge(new DagEdge { FromId = "b", ToId = "c" });

        Assert.False(dag.HasCycle());
    }

    [Fact]
    public void TopologicalSort_ShouldReturnCorrectOrder()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        dag.AddNode(new DagNode<string> { Id = "b", Payload = "B" });
        dag.AddNode(new DagNode<string> { Id = "c", Payload = "C" });
        dag.AddEdge(new DagEdge { FromId = "a", ToId = "b" });
        dag.AddEdge(new DagEdge { FromId = "b", ToId = "c" });

        var sorted = dag.TopologicalSort();
        Assert.Equal(3, sorted.Count);
        Assert.Equal("a", sorted[0].Id);
        Assert.Equal("b", sorted[1].Id);
        Assert.Equal("c", sorted[2].Id);
    }

    [Fact]
    public void TopologicalSort_Diamond_ShouldRespectDependencies()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        dag.AddNode(new DagNode<string> { Id = "b", Payload = "B" });
        dag.AddNode(new DagNode<string> { Id = "c", Payload = "C" });
        dag.AddNode(new DagNode<string> { Id = "d", Payload = "D" });
        dag.AddEdge(new DagEdge { FromId = "a", ToId = "b" });
        dag.AddEdge(new DagEdge { FromId = "a", ToId = "c" });
        dag.AddEdge(new DagEdge { FromId = "b", ToId = "d" });
        dag.AddEdge(new DagEdge { FromId = "c", ToId = "d" });

        var sorted = dag.TopologicalSort();
        var order = sorted.Select(n => n.Id).ToList();
        Assert.Equal("a", order[0]);
        Assert.Equal("d", order[3]);
        Assert.True(order.IndexOf("b") < order.IndexOf("d"));
        Assert.True(order.IndexOf("c") < order.IndexOf("d"));
    }

    [Fact]
    public void GetDescendants_ShouldReturnAllDownstream()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        dag.AddNode(new DagNode<string> { Id = "b", Payload = "B" });
        dag.AddNode(new DagNode<string> { Id = "c", Payload = "C" });
        dag.AddEdge(new DagEdge { FromId = "a", ToId = "b" });
        dag.AddEdge(new DagEdge { FromId = "b", ToId = "c" });

        var descendants = dag.GetDescendants("a");
        Assert.Equal(2, descendants.Count);
        Assert.Contains(descendants, d => d.Id == "b");
        Assert.Contains(descendants, d => d.Id == "c");
    }

    [Fact]
    public void GetAncestors_ShouldReturnAllUpstream()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        dag.AddNode(new DagNode<string> { Id = "b", Payload = "B" });
        dag.AddNode(new DagNode<string> { Id = "c", Payload = "C" });
        dag.AddEdge(new DagEdge { FromId = "a", ToId = "b" });
        dag.AddEdge(new DagEdge { FromId = "b", ToId = "c" });

        var ancestors = dag.GetAncestors("c");
        Assert.Equal(2, ancestors.Count);
        Assert.Contains(ancestors, d => d.Id == "a");
        Assert.Contains(ancestors, d => d.Id == "b");
    }

    [Fact]
    public void GetAffectedSubgraph_ShouldReturnOnlyDescendants()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        dag.AddNode(new DagNode<string> { Id = "b", Payload = "B" });
        dag.AddNode(new DagNode<string> { Id = "c", Payload = "C" });
        dag.AddNode(new DagNode<string> { Id = "d", Payload = "D" });
        dag.AddEdge(new DagEdge { FromId = "a", ToId = "b" });
        dag.AddEdge(new DagEdge { FromId = "b", ToId = "c" });

        var affected = dag.GetAffectedSubgraph("a");
        var affectedIds = affected.Select(n => n.Id).ToHashSet();
        Assert.Contains("a", affectedIds);
        Assert.Contains("b", affectedIds);
        Assert.Contains("c", affectedIds);
        Assert.DoesNotContain("d", affectedIds);
    }

    [Fact]
    public void RemoveNode_ShouldRemoveAssociatedEdges()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        dag.AddNode(new DagNode<string> { Id = "b", Payload = "B" });
        dag.AddNode(new DagNode<string> { Id = "c", Payload = "C" });
        dag.AddEdge(new DagEdge { FromId = "a", ToId = "b" });
        dag.AddEdge(new DagEdge { FromId = "b", ToId = "c" });

        dag.RemoveNode("b");

        Assert.Equal(2, dag.Nodes.Count);
        Assert.Empty(dag.Edges);
        Assert.Empty(dag.Nodes["a"].OutEdgeIds);
        Assert.Empty(dag.Nodes["c"].InEdgeIds);
    }

    [Fact]
    public void FindAllCycles_ShouldDetectCycleWhenEdgeRejected()
    {
        var dag = new Dag<string>();
        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        dag.AddNode(new DagNode<string> { Id = "b", Payload = "B" });
        dag.AddEdge(new DagEdge { FromId = "a", ToId = "b" });

        var result = dag.AddEdge(new DagEdge { FromId = "b", ToId = "a" });
        Assert.False(result.Success);
        Assert.NotNull(result.CyclePath);
        Assert.True(result.CyclePath.Count >= 2);
    }

    [Fact]
    public void Version_ShouldIncrementOnMutation()
    {
        var dag = new Dag<string>();
        var v0 = dag.Version;

        dag.AddNode(new DagNode<string> { Id = "a", Payload = "A" });
        var v1 = dag.Version;
        Assert.NotEqual(v0, v1);

        dag.AddNode(new DagNode<string> { Id = "b", Payload = "B" });
        dag.AddEdge(new DagEdge { FromId = "a", ToId = "b" });
        var v2 = dag.Version;
        Assert.NotEqual(v1, v2);
    }
}
