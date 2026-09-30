namespace Core.Goal.Tests;


/// <summary>
/// GoalGraphEngine 时序测试 — 需 Task.Delay/并发竞争/超时窗口的测试集中于此 partial。
/// <para>拆分自 GoalGraphEngineTests.cs 与 GoalGraphEngineTests.EventDriven.cs,</para>
/// <para>确定性测试留在原文件并标 [Trait("Category","Deterministic")]。</para>
/// </summary>
public sealed partial class GoalGraphEngineTests {
    // ─────────────────────────────────────────────────────────────
    // 7. 节点超时：TimeoutSeconds=1 的节点执行超时 → 标记 Failed
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Timing")]
    public async Task NodeTimeout_Should_MarkAsFailed_WhenExceedsTimeout() {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = new DagNode<GoalNodePayload> {
            Id = "A",
            Payload = new GoalNodePayload {
                Kind = GoalNodeKind.Function,
                Name = "slow-node",
                TimeoutSeconds = 1, // 1 秒超时
            },
        };

        dag.AddNode(nodeA);

        // 注册一个延迟 5 秒的函数（会因超时被取消）
        engine.RegisterFunction("A", async ctx => {
            try {
                await Task.Delay(TimeSpan.FromSeconds(5), ctx.CancellationToken);
            } catch (OperationCanceledException) {
                // 重新抛出，让引擎捕获超时
                throw;
            }

            return NodeResult.Succeeded("should-not-reach");
        });

        var graph = new GoalGraph {
            Name = "timeout-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("A"),
        };

        var goalState = CreateGoalState();
        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);

        Assert.Equal(GoalNodeStatus.Failed, nodeA.Payload.Status);
        Assert.Contains("Timeout", nodeA.Payload.ErrorMessage);
        Assert.Equal(GoalStatus.Unmet, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 补充：取消令牌 — 执行中途取消应抛出 OperationCanceledException
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Timing")]
    public async Task CancellationMidExecution_Should_ThrowOperationCanceledException() {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "slow-node");

        dag.AddNode(nodeA);

        using var cts = new CancellationTokenSource();

        engine.RegisterFunction("A", async ctx => {
            // 延迟后取消
            await Task.Delay(100, CancellationToken.None);
            cts.Cancel();
            // 再延迟让取消传播
            await Task.Delay(100, ctx.CancellationToken);
            return NodeResult.Succeeded("done");
        });

        var graph = new GoalGraph {
            Name = "cancel-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("A"),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), cts.Token));
    }

    // ─────────────────────────────────────────────────────────────
    // P0-1 真正并行执行：A→[B,C]→J，B 和 C 应并发执行（maxConcurrent >= 2）
    // 串行队列下 maxConcurrent 恒为 1，此测试验证真正并行
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Timing")]
    public async Task ParallelExecution_Should_RunIndependentNodesConcurrently() {
        var engine = CreateEngine();
        var concurrentCount = 0;
        var maxConcurrent = 0;

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "source");
        var nodeB = MakeFunctionNode("B", "branch-b");
        var nodeC = MakeFunctionNode("C", "branch-c");
        var nodeJ = MakeJoinNode("J", "join");

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeC);
        dag.AddNode(nodeJ);
        dag.AddEdge(new DagEdge { Id = "e-a-b", FromId = "A", ToId = "B" });
        dag.AddEdge(new DagEdge { Id = "e-a-c", FromId = "A", ToId = "C" });
        dag.AddEdge(new DagEdge { Id = "e-b-j", FromId = "B", ToId = "J" });
        dag.AddEdge(new DagEdge { Id = "e-c-j", FromId = "C", ToId = "J" });

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Succeeded("output-A", tokensUsed: 10)));

        engine.RegisterFunction("B", async _ => {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(150, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-B", tokensUsed: 20);
        });

        engine.RegisterFunction("C", async _ => {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(150, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-C", tokensUsed: 30);
        });

        var graph = new GoalGraph {
            Name = "parallel-execution-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalNodeStatus.Completed, nodeB.Payload.Status);
        Assert.Equal(GoalNodeStatus.Completed, nodeC.Payload.Status);
        Assert.Equal(GoalStatus.Achieved, result.Status);
        Assert.True(maxConcurrent >= 2, $"B 和 C 应并发执行，但 maxConcurrent={maxConcurrent}（串行执行）");
    }

    // ─────────────────────────────────────────────────────────────
    // P0-1 并行限流：MaxConcurrency=1 时退化为串行（maxConcurrent == 1）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Timing")]
    public async Task ParallelExecution_WithMaxConcurrency1_Should_DegradeToSerial() {
        var engine = CreateEngine(concurrencyOptions: new SubAgentConcurrencyOptions { MaxConcurrentExecutions = 1 });
        var concurrentCount = 0;
        var maxConcurrent = 0;

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "source");
        var nodeB = MakeFunctionNode("B", "branch-b");
        var nodeC = MakeFunctionNode("C", "branch-c");
        var nodeJ = MakeJoinNode("J", "join");

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeC);
        dag.AddNode(nodeJ);
        dag.AddEdge(new DagEdge { Id = "e-a-b", FromId = "A", ToId = "B" });
        dag.AddEdge(new DagEdge { Id = "e-a-c", FromId = "A", ToId = "C" });
        dag.AddEdge(new DagEdge { Id = "e-b-j", FromId = "B", ToId = "J" });
        dag.AddEdge(new DagEdge { Id = "e-c-j", FromId = "C", ToId = "J" });

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Succeeded("output-A", tokensUsed: 10)));

        engine.RegisterFunction("B", async _ => {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(100, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-B", tokensUsed: 20);
        });

        engine.RegisterFunction("C", async _ => {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(100, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-C", tokensUsed: 30);
        });

        var graph = new GoalGraph {
            Name = "parallel-max1-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalStatus.Achieved, result.Status);
        Assert.True(maxConcurrent == 1, $"MaxConcurrentExecutions=1 应串行，但 maxConcurrent={maxConcurrent}");
    }

    // ─────────────────────────────────────────────────────────────
    // P1-4 失败率终止：B/C 失败，D/E 成功，失败率>50% 时终止为 Unmet
    // B/C 立即失败先完成，D/E 延迟100ms，C 完成时 2/3=66%>50% 触发
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Timing")]
    public async Task HighFailureRate_Should_TerminateAsUnmet() {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "source");
        var nodeB = MakeFunctionNode("B", "fail-b");
        var nodeC = MakeFunctionNode("C", "fail-c");
        var nodeD = MakeFunctionNode("D", "ok-d");
        var nodeE = MakeFunctionNode("E", "ok-e");
        var nodeJ = MakeJoinNode("J", "join", minSuccessfulInputs: 2);

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeC);
        dag.AddNode(nodeD);
        dag.AddNode(nodeE);
        dag.AddNode(nodeJ);
        dag.AddEdge(new DagEdge { Id = "e1", FromId = "A", ToId = "B" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "A", ToId = "C" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "A", ToId = "D" });
        dag.AddEdge(new DagEdge { Id = "e4", FromId = "A", ToId = "E" });
        dag.AddEdge(new DagEdge { Id = "e5", FromId = "B", ToId = "J" });
        dag.AddEdge(new DagEdge { Id = "e6", FromId = "C", ToId = "J" });
        dag.AddEdge(new DagEdge { Id = "e7", FromId = "D", ToId = "J" });
        dag.AddEdge(new DagEdge { Id = "e8", FromId = "E", ToId = "J" });

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Succeeded("output-A", tokensUsed: 10)));
        engine.RegisterFunction("B", _ =>
            Task.FromResult(NodeResult.Failed("B-failed", tokensUsed: 5)));
        engine.RegisterFunction("C", _ =>
            Task.FromResult(NodeResult.Failed("C-failed", tokensUsed: 5)));
        engine.RegisterFunction("D", async _ => {
            await Task.Delay(100, CancellationToken.None);
            return NodeResult.Succeeded("D-ok", tokensUsed: 10);
        });
        engine.RegisterFunction("E", async _ => {
            await Task.Delay(100, CancellationToken.None);
            return NodeResult.Succeeded("E-ok", tokensUsed: 10);
        });

        var graph = new GoalGraph {
            Name = "high-failure-rate-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalStatus.Unmet, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // EventDriven 2. 并行执行：菱形 A → {B, C} → D（EventDriven 版）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Timing")]
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
    // EventDriven 4. 并发10次无死锁（EventDriven 版）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Timing")]
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
    // EventDriven 5. 取消令牌传播（EventDriven 版）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    [Trait("Category", "Timing")]
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
