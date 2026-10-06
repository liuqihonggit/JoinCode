namespace Core.Goal.Tests;


/// <summary>
/// EventDrivenGraphScheduler 行为等价测试 — 验证 Channel 驱动调度与轮询式调度行为一致。
/// <para>确定性部分(纯 Task.FromResult,不涉 Task.Delay/并发竞争)。时序部分见 GoalGraphEngineTests.Timing.cs。</para>
/// </summary>
public sealed partial class GoalGraphEngineTests {
    private static GoalGraphEngine CreateEventDrivenEngine(
        Mock<IChatClient>? kernel = null,
        Mock<IGoalEvaluator>? evaluator = null,
        IClockService? clock = null,
        IServiceProvider? serviceProvider = null,
        IGoalUserInteraction? userInteraction = null,
        IGoalNodeInspector? nodeInspector = null,
        IGoalConflictMessenger? conflictMessenger = null,
        SubAgentConcurrencyOptions? concurrencyOptions = null) {
        return new GoalGraphEngine(
            (kernel ?? CreateKernelMock()).Object,
            (evaluator ?? CreateEvaluatorMock()).Object,
            serviceProvider ?? new ServiceCollection().BuildServiceProvider(),
            heartbeat: CreateHeartbeatMock().Object,
            clock: clock,
            userInteraction: userInteraction,
            nodeInspector: nodeInspector,
            conflictMessenger: conflictMessenger,
            concurrencyOptions: concurrencyOptions,
            graphScheduler: new EventDrivenGraphScheduler());
    }

    // ─────────────────────────────────────────────────────────────
    // 1. 串行执行：A → B → C（EventDriven 版）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Deterministic")]
    public async Task EventDriven_SerialExecution_Should_ExecuteInOrder_AndPassOutput() {
        await using var engine = CreateEventDrivenEngine();
        var executionOrder = new List<string>();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "step-a");
        var nodeB = MakeFunctionNode("B", "step-b");
        var nodeC = MakeFunctionNode("C", "step-c");

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeC);
        dag.AddEdge(new DagEdge { Id = "e-ab", FromId = "A", ToId = "B" });
        dag.AddEdge(new DagEdge { Id = "e-bc", FromId = "B", ToId = "C" });

        engine.RegisterFunction("A", _ => {
            executionOrder.Add("A");
            return Task.FromResult(NodeResult.Succeeded("output-A", tokensUsed: 10));
        });

        engine.RegisterFunction("B", ctx => {
            executionOrder.Add("B");
            var upstreamA = ctx.UpstreamOutputs.GetValueOrDefault("A", null);
            var output = $"B-received-{upstreamA}";
            return Task.FromResult(NodeResult.Succeeded(output, tokensUsed: 20));
        });

        engine.RegisterFunction("C", ctx => {
            executionOrder.Add("C");
            var upstreamB = ctx.UpstreamOutputs.GetValueOrDefault("B", null);
            var output = $"C-received-{upstreamB}";
            return Task.FromResult(NodeResult.Succeeded(output, tokensUsed: 30));
        });

        var graph = new GoalGraph {
            Name = "event-driven-serial",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("C"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(["A", "B", "C"], executionOrder);
        Assert.Equal("output-A", nodeA.Payload.Output);
        Assert.Equal("B-received-output-A", nodeB.Payload.Output);
        Assert.Equal("C-received-B-received-output-A", nodeC.Payload.Output);
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 失败终止：A 失败且为 EndNode → GoalUnmet（EventDriven 版）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Deterministic")]
    public async Task EventDriven_FailedEndNode_Should_SetGoalUnmet() {
        await using var engine = CreateEventDrivenEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "fail-end");

        dag.AddNode(nodeA);

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Failed("intentional failure")));

        var graph = new GoalGraph {
            Name = "event-driven-fail-end",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("A"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalNodeStatus.Failed, nodeA.Payload.Status);
        Assert.Equal("intentional failure", nodeA.Payload.ErrorMessage);
        Assert.Equal(GoalStatus.Unmet, result.Status);
    }
}
