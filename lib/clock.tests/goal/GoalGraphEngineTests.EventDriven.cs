namespace Core.Goal.Tests;


/// <summary>
/// EventDrivenGraphScheduler 行为等价测试 — 验证 Channel 驱动调度与轮询式调度行为一致。
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
    public async Task EventDriven_SerialExecution_Should_ExecuteInOrder_AndPassOutput() {
        var engine = CreateEventDrivenEngine();
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
    // 2. 并行执行：菱形 A → {B, C} → D（EventDriven 版）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EventDriven_ParallelDiamond_Should_ExecuteBAndC_InParallel() {
        var engine = CreateEventDrivenEngine();
        var executedNodes = new ConcurrentBag<string>();
        var concurrencyOptions = new SubAgentConcurrencyOptions { MaxConcurrentExecutions = 2 };
        engine.UpdateConcurrencyOptions(concurrencyOptions);

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "start");
        var nodeB = MakeFunctionNode("B", "branch-1");
        var nodeC = MakeFunctionNode("C", "branch-2");
        var nodeD = MakeFunctionNode("D", "join");

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeC);
        dag.AddNode(nodeD);
        dag.AddEdge(new DagEdge { Id = "e-ab", FromId = "A", ToId = "B" });
        dag.AddEdge(new DagEdge { Id = "e-ac", FromId = "A", ToId = "C" });
        dag.AddEdge(new DagEdge { Id = "e-bd", FromId = "B", ToId = "D" });
        dag.AddEdge(new DagEdge { Id = "e-cd", FromId = "C", ToId = "D" });

        engine.RegisterFunction("A", _ => {
            executedNodes.Add("A");
            return Task.FromResult(NodeResult.Succeeded("A-out", tokensUsed: 5));
        });
        engine.RegisterFunction("B", async _ => {
            executedNodes.Add("B");
            await Task.Delay(50);
            return NodeResult.Succeeded("B-out", tokensUsed: 10);
        });
        engine.RegisterFunction("C", async _ => {
            executedNodes.Add("C");
            await Task.Delay(30);
            return NodeResult.Succeeded("C-out", tokensUsed: 15);
        });
        engine.RegisterFunction("D", _ => {
            executedNodes.Add("D");
            return Task.FromResult(NodeResult.Succeeded("D-out", tokensUsed: 20));
        });

        var graph = new GoalGraph {
            Name = "event-driven-diamond",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("D"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalStatus.Achieved, result.Status);
        Assert.Contains("A", executedNodes);
        Assert.Contains("B", executedNodes);
        Assert.Contains("C", executedNodes);
        Assert.Contains("D", executedNodes);
        Assert.Equal(GoalNodeStatus.Completed, nodeD.Payload.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 失败终止：A 失败且为 EndNode → GoalUnmet（EventDriven 版）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EventDriven_FailedEndNode_Should_SetGoalUnmet() {
        var engine = CreateEventDrivenEngine();

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

    // ─────────────────────────────────────────────────────────────
    // 4. 并发100次无死锁（EventDriven 版）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EventDriven_Concurrent10Runs_Should_NoDeadlock() {
        var engine = CreateEventDrivenEngine();
        engine.RegisterFunction("A", _ => Task.FromResult(NodeResult.Succeeded("A-out", tokensUsed: 1)));
        engine.RegisterFunction("B", _ => Task.FromResult(NodeResult.Succeeded("B-out", tokensUsed: 1)));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var tasks = Enumerable.Range(0, 10).Select(_ => {
            var dag = new Dag<GoalNodePayload>();
            dag.AddNode(MakeFunctionNode("A", "step-a"));
            dag.AddNode(MakeFunctionNode("B", "step-b"));
            dag.AddEdge(new DagEdge { Id = "e-ab", FromId = "A", ToId = "B" });
            var graph = new GoalGraph {
                Name = "event-driven-concurrent",
                Dag = dag,
                StartNodeId = "A",
                EndNodeIds = FrozenSet.Create("B"),
            };
            return engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), cts.Token);
        });

        var results = await Task.WhenAll(tasks);

        Assert.All(results, r => Assert.Equal(GoalStatus.Achieved, r.Status));
    }

    // ─────────────────────────────────────────────────────────────
    // 5. 取消令牌传播（EventDriven 版）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task EventDriven_Cancellation_Should_PropagateAndStop() {
        var engine = CreateEventDrivenEngine();
        var executedNodes = new List<string>();

        var dag = new Dag<GoalNodePayload>();
        dag.AddNode(MakeFunctionNode("A", "step-a"));
        dag.AddNode(MakeFunctionNode("B", "step-b"));
        dag.AddEdge(new DagEdge { Id = "e-ab", FromId = "A", ToId = "B" });

        engine.RegisterFunction("A", async _ => {
            executedNodes.Add("A");
            await Task.Delay(100);
            return NodeResult.Succeeded("A-out");
        });
        engine.RegisterFunction("B", _ => {
            executedNodes.Add("B");
            return Task.FromResult(NodeResult.Succeeded("B-out"));
        });

        var graph = new GoalGraph {
            Name = "event-driven-cancel",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("B"),
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), cts.Token));
    }
}