// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Goal.Tests;

/// <summary>
/// GraphExecutionContext.CollectNextNodeIds 纯函数确定性测试 — 不依赖时序,不依赖实例状态。
/// </summary>
public sealed class GraphExecutionContextTest {
    /// <summary>
    /// 构造测试 DAG:a → b(无条件), a → c(PASS), a → d(FAIL)
    /// </summary>
    private static Dag<GoalNodePayload> BuildTestDag() {
        var dag = new Dag<GoalNodePayload>();
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "a", Payload = new() { Kind = GoalNodeKind.Function, Name = "a" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "b", Payload = new() { Kind = GoalNodeKind.Function, Name = "b" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "c", Payload = new() { Kind = GoalNodeKind.Function, Name = "c" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "d", Payload = new() { Kind = GoalNodeKind.Function, Name = "d" } });
        dag.AddEdge(new DagEdge { Id = "e1", FromId = "a", ToId = "b" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "a", ToId = "c", Label = "PASS" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "a", ToId = "d", Label = "FAIL" });
        return dag;
    }

    [Fact]
    public void CollectNextNodeIds_UnconditionalOnly_NoRoutes_Should_Return_Only_Unlabeled_Edges() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "a", null, RouteMatchMode.UnconditionalOnly);

        Assert.Single(next);
        Assert.Contains("b", next);
    }

    [Fact]
    public void CollectNextNodeIds_UnconditionalOnly_WithRoutes_Should_Return_Unlabeled_And_Matching_Labeled() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "a", ["PASS"], RouteMatchMode.UnconditionalOnly);

        Assert.Equal(2, next.Count);
        Assert.Contains("b", next);
        Assert.Contains("c", next);
    }

    [Fact]
    public void CollectNextNodeIds_ConditionalOnly_MatchingRoute_Should_Return_Only_Matching_Labeled_Edges() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "a", ["PASS"], RouteMatchMode.ConditionalOnly);

        Assert.Single(next);
        Assert.Contains("c", next);
    }

    [Fact]
    public void CollectNextNodeIds_ConditionalOnly_MatchingFailRoute_Should_Return_Only_Fail_Edge() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "a", ["FAIL"], RouteMatchMode.ConditionalOnly);

        Assert.Single(next);
        Assert.Contains("d", next);
    }

    [Fact]
    public void CollectNextNodeIds_ConditionalOnly_MultipleMatchingRoutes_Should_Return_All_Matching() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "a", ["PASS", "FAIL"], RouteMatchMode.ConditionalOnly);

        Assert.Equal(2, next.Count);
        Assert.Contains("c", next);
        Assert.Contains("d", next);
    }

    [Fact]
    public void CollectNextNodeIds_ConditionalOnly_NoRoutes_Should_Fallback_To_Unlabeled_Edges() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "a", null, RouteMatchMode.ConditionalOnly);

        Assert.Single(next);
        Assert.Contains("b", next);
    }

    [Fact]
    public void CollectNextNodeIds_ConditionalOnly_UnknownRoute_Should_Fallback_To_Unlabeled_Edges() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "a", ["UNKNOWN"], RouteMatchMode.ConditionalOnly);

        Assert.Single(next);
        Assert.Contains("b", next);
    }

    [Fact]
    public void CollectNextNodeIds_All_NoRoutes_Should_Return_Only_Unlabeled_Edges() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "a", null, RouteMatchMode.All);

        Assert.Single(next);
        Assert.Contains("b", next);
    }

    [Fact]
    public void CollectNextNodeIds_All_WithRoutes_Should_Return_Unlabeled_And_Matching_Labeled() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "a", ["PASS"], RouteMatchMode.All);

        Assert.Equal(2, next.Count);
        Assert.Contains("b", next);
        Assert.Contains("c", next);
    }

    [Fact]
    public void CollectNextNodeIds_All_WithAllRoutes_Should_Return_All_Edges() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "a", ["PASS", "FAIL"], RouteMatchMode.All);

        Assert.Equal(3, next.Count);
        Assert.Contains("b", next);
        Assert.Contains("c", next);
        Assert.Contains("d", next);
    }

    [Fact]
    public void CollectNextNodeIds_NonExistentNode_Should_Return_Empty() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "nonexistent", null, RouteMatchMode.All);

        Assert.Empty(next);
    }

    [Fact]
    public void CollectNextNodeIds_NodeWithNoOutEdges_Should_Return_Empty() {
        var dag = BuildTestDag();

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "b", null, RouteMatchMode.All);

        Assert.Empty(next);
    }

    [Fact]
    public void CollectNextNodeIds_OnlyLabeledEdges_ConditionalOnly_NoMatch_Should_Return_Empty() {
        var dag = new Dag<GoalNodePayload>();
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "x", Payload = new() { Kind = GoalNodeKind.Function, Name = "x" } });
        dag.AddNode(new DagNode<GoalNodePayload> { Id = "y", Payload = new() { Kind = GoalNodeKind.Function, Name = "y" } });
        dag.AddEdge(new DagEdge { Id = "e1", FromId = "x", ToId = "y", Label = "PASS" });

        var next = GraphExecutionContext.CollectNextNodeIds(dag, "x", ["FAIL"], RouteMatchMode.ConditionalOnly);

        Assert.Empty(next);
    }

    [Fact]
    public void CollectNextNodeIds_Should_Be_Deterministic_For_Same_Inputs() {
        var dag = BuildTestDag();

        var next1 = GraphExecutionContext.CollectNextNodeIds(dag, "a", ["PASS"], RouteMatchMode.All);
        var next2 = GraphExecutionContext.CollectNextNodeIds(dag, "a", ["PASS"], RouteMatchMode.All);

        Assert.Equal(next1, next2);
    }

    [Fact]
    public void CollectNextNodeIds_NullDag_Should_Throw_ArgumentNullException() {
        Assert.Throws<ArgumentNullException>(() =>
            GraphExecutionContext.CollectNextNodeIds(null!, "a", null, RouteMatchMode.All));
    }

    [Fact]
    public void CollectNextNodeIds_NullFromNodeId_Should_Throw_ArgumentNullException() {
        var dag = BuildTestDag();

        Assert.Throws<ArgumentNullException>(() =>
            GraphExecutionContext.CollectNextNodeIds(dag, null!, null, RouteMatchMode.All));
    }

    [Fact]
    public void CollectNextNodeIds_EmptyFromNodeId_Should_Throw_ArgumentException() {
        var dag = BuildTestDag();

        Assert.Throws<ArgumentException>(() =>
            GraphExecutionContext.CollectNextNodeIds(dag, "", null, RouteMatchMode.All));
    }

    [Fact]
    public void CollectNextNodeIds_WhiteSpaceFromNodeId_Should_Throw_ArgumentException() {
        var dag = BuildTestDag();

        Assert.Throws<ArgumentException>(() =>
            GraphExecutionContext.CollectNextNodeIds(dag, "   ", null, RouteMatchMode.All));
    }
}
