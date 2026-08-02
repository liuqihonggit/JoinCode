
namespace Core.Goal.Tests;

using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using Structura.Dag;
using Infrastructure.Time;

public sealed class GoalGraphEngineTests
{
    /// <summary>
    /// 创建 Mock IChatClient — Agent 节点测试时需额外配置 GetChatCompletionService
    /// </summary>
    private static Mock<IChatClient> CreateKernelMock()
    {
        var kernel = new Mock<IChatClient>();
        var plugins = new Mock<IToolCollection>();
        kernel.SetupGet(k => k.Plugins).Returns(plugins.Object);
        return kernel;
    }

    private static Mock<IGoalEvaluator> CreateEvaluatorMock() => new();

    private static Mock<IGoalHeartbeat> CreateHeartbeatMock()
    {
        var heartbeat = new Mock<IGoalHeartbeat>();
        heartbeat.SetupGet(h => h.RefCount).Returns(0);
        heartbeat.SetupGet(h => h.IsActive).Returns(false);
        heartbeat.Setup(h => h.RegisterCallback(It.IsAny<Func<CancellationToken, ValueTask>>()));
        heartbeat.Setup(h => h.DisposeAsync()).Returns(new ValueTask());
        return heartbeat;
    }

    private static GoalGraphEngine CreateEngine(
        Mock<IChatClient>? kernel = null,
        Mock<IGoalEvaluator>? evaluator = null,
        IClockService? clock = null,
        IServiceProvider? serviceProvider = null)
    {
        return new GoalGraphEngine(
            (kernel ?? CreateKernelMock()).Object,
            (evaluator ?? CreateEvaluatorMock()).Object,
            serviceProvider ?? new ServiceCollection().BuildServiceProvider(),
            heartbeat: CreateHeartbeatMock().Object,
            clock: clock);
    }

    private static GoalState CreateGoalState() => new()
    {
        GoalId = "test-goal",
        Objective = "test objective",
    };

    private static DagNode<GoalNodePayload> MakeFunctionNode(string id, string name, int timeoutSeconds = 300)
        => new()
        {
            Id = id,
            Payload = new GoalNodePayload
            {
                Kind = GoalNodeKind.Function,
                Name = name,
                TimeoutSeconds = timeoutSeconds,
            },
        };

    private static DagNode<GoalNodePayload> MakeJoinNode(string id, string name, int minSuccessfulInputs = 0)
        => new()
        {
            Id = id,
            Payload = new GoalNodePayload
            {
                Kind = GoalNodeKind.Join,
                Name = name,
                MinSuccessfulInputs = minSuccessfulInputs,
            },
        };

    // ─────────────────────────────────────────────────────────────
    // 1. 串行执行：A → B → C，验证执行顺序和 Output 传递
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SerialExecution_Should_ExecuteInOrder_AndPassOutput()
    {
        var engine = CreateEngine();
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

        engine.RegisterFunction("A", _ =>
        {
            executionOrder.Add("A");
            return Task.FromResult(NodeResult.Succeeded("output-A", tokensUsed: 10));
        });

        engine.RegisterFunction("B", ctx =>
        {
            executionOrder.Add("B");
            var upstreamA = ctx.UpstreamOutputs.GetValueOrDefault("A", null);
            var output = $"B-received-{upstreamA}";
            return Task.FromResult(NodeResult.Succeeded(output, tokensUsed: 20));
        });

        engine.RegisterFunction("C", ctx =>
        {
            executionOrder.Add("C");
            var upstreamB = ctx.UpstreamOutputs.GetValueOrDefault("B", null);
            var output = $"C-received-{upstreamB}";
            return Task.FromResult(NodeResult.Succeeded(output, tokensUsed: 30));
        });

        var graph = new GoalGraph
        {
            Name = "serial-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("C"),
        };

        var goalState = CreateGoalState();
        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);

        // 验证执行顺序
        Assert.Equal(["A", "B", "C"], executionOrder);

        // 验证 Output 传递
        Assert.Equal("output-A", nodeA.Payload.Output);
        Assert.Equal("B-received-output-A", nodeB.Payload.Output);
        Assert.Equal("C-received-B-received-output-A", nodeC.Payload.Output);

        // 验证最终状态
        Assert.Equal(GoalStatus.Achieved, result.Status);
        Assert.Equal(GoalNodeStatus.Completed, nodeA.Payload.Status);
        Assert.Equal(GoalNodeStatus.Completed, nodeB.Payload.Status);
        Assert.Equal(GoalNodeStatus.Completed, nodeC.Payload.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 2. 条件路由：节点 Routes=["PASS"]，只走 PASS 边
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConditionalRouting_Should_OnlyFollowMatchedEdge()
    {
        var engine = CreateEngine();
        var executedNodes = new List<string>();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "router");
        var nodeB = MakeFunctionNode("B", "pass-handler");
        var nodeC = MakeFunctionNode("C", "fail-handler");

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeC);
        dag.AddEdge(new DagEdge { Id = "e-pass", FromId = "A", ToId = "B", Label = "PASS" });
        dag.AddEdge(new DagEdge { Id = "e-fail", FromId = "A", ToId = "C", Label = "FAIL" });

        engine.RegisterFunction("A", _ =>
        {
            executedNodes.Add("A");
            return Task.FromResult(NodeResult.Routed("A-done", ["PASS"], tokensUsed: 5));
        });

        engine.RegisterFunction("B", _ =>
        {
            executedNodes.Add("B");
            return Task.FromResult(NodeResult.Succeeded("B-done", tokensUsed: 5));
        });

        engine.RegisterFunction("C", _ =>
        {
            executedNodes.Add("C");
            return Task.FromResult(NodeResult.Succeeded("C-done", tokensUsed: 5));
        });

        var graph = new GoalGraph
        {
            Name = "conditional-routing-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("B", "C"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Contains("A", executedNodes);
        Assert.Contains("B", executedNodes);
        Assert.DoesNotContain("C", executedNodes);

        Assert.Equal(GoalNodeStatus.Completed, nodeA.Payload.Status);
        Assert.Equal(GoalNodeStatus.Completed, nodeB.Payload.Status);
        Assert.Equal(GoalNodeStatus.Pending, nodeC.Payload.Status); // 未执行，保持 Pending
    }

    // ─────────────────────────────────────────────────────────────
    // 3. 条件路由兜底：Routes 不匹配任何条件边时，走空 Label 边
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConditionalRoutingFallback_Should_TakeEmptyLabelEdge_WhenNoMatch()
    {
        var engine = CreateEngine();
        var executedNodes = new List<string>();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "router");
        var nodeB = MakeFunctionNode("B", "pass-handler");
        var nodeC = MakeFunctionNode("C", "fallback-handler");

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeC);
        dag.AddEdge(new DagEdge { Id = "e-pass", FromId = "A", ToId = "B", Label = "PASS" });
        dag.AddEdge(new DagEdge { Id = "e-fallback", FromId = "A", ToId = "C" }); // 空 Label = 兜底

        engine.RegisterFunction("A", _ =>
        {
            executedNodes.Add("A");
            return Task.FromResult(NodeResult.Routed("A-done", ["UNKNOWN"], tokensUsed: 5));
        });

        engine.RegisterFunction("B", _ =>
        {
            executedNodes.Add("B");
            return Task.FromResult(NodeResult.Succeeded("B-done"));
        });

        engine.RegisterFunction("C", _ =>
        {
            executedNodes.Add("C");
            return Task.FromResult(NodeResult.Succeeded("C-done"));
        });

        var graph = new GoalGraph
        {
            Name = "fallback-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("B", "C"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Contains("A", executedNodes);
        Assert.DoesNotContain("B", executedNodes); // PASS 边不匹配
        Assert.Contains("C", executedNodes);       // 空 Label 兜底边

        Assert.Equal(GoalNodeStatus.Completed, nodeC.Payload.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 4. 回退重激活：test 节点 Routes=["FAIL"] → 重激活 implement 节点
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryReactivation_Should_ResetAndReexecuteTargetNode()
    {
        var engine = CreateEngine();
        var implementCallCount = 0;
        var testCallCount = 0;

        var dag = new Dag<GoalNodePayload>();
        var nodeImpl = MakeFunctionNode("implement", "implement-step");
        var nodeTest = MakeFunctionNode("test", "test-step");

        dag.AddNode(nodeImpl);
        dag.AddNode(nodeTest);
        dag.AddEdge(new DagEdge { Id = "e-impl-test", FromId = "implement", ToId = "test" });
        // 回退边：test → implement（允许环，用于重激活）
        // 注意：TryAddEdge 会给 implement 添加入边，导致 AreAllUpstreamsCompleted 死锁。
        // 回退边不应参与上游依赖检查，因此从 InEdgeIds 中移除。
        const string backEdgeId = "e-test-impl";
        dag.TryAddEdge(new DagEdge { Id = backEdgeId, FromId = "test", ToId = "implement", Label = "FAIL" });
        nodeImpl.InEdgeIds.Remove(backEdgeId);

        engine.RegisterFunction("implement", _ =>
        {
            implementCallCount++;
            return Task.FromResult(NodeResult.Succeeded($"implement-v{implementCallCount}", tokensUsed: 15));
        });

        engine.RegisterFunction("test", _ =>
        {
            testCallCount++;
            // 第一次 FAIL，第二次通过（返回空 Routes，走兜底或无后续边）
            if (testCallCount == 1)
            {
                return Task.FromResult(NodeResult.Routed("test-fail", ["FAIL"], tokensUsed: 10));
            }

            return Task.FromResult(NodeResult.Succeeded("test-pass", tokensUsed: 10));
        });

        var graph = new GoalGraph
        {
            Name = "retry-test",
            Dag = dag,
            StartNodeId = "implement",
            EndNodeIds = FrozenSet.Create("test"),
            MaxRetriesPerNode = 3,
        };

        var goalState = CreateGoalState();
        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);

        // implement 被执行 2 次（初始 + 重激活 1 次）
        Assert.Equal(2, implementCallCount);
        // test 被执行 2 次（初始 FAIL + 重激活后 PASS）
        Assert.Equal(2, testCallCount);

        // 最终都完成
        Assert.Equal(GoalNodeStatus.Completed, nodeImpl.Payload.Status);
        Assert.Equal(GoalNodeStatus.Completed, nodeTest.Payload.Status);
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 5. 回退超过最大重试 → 节点标记 Failed
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RetryExceedsMax_Should_MarkNodeAsFailed()
    {
        var engine = CreateEngine();
        var implementCallCount = 0;

        var dag = new Dag<GoalNodePayload>();
        var nodeImpl = MakeFunctionNode("implement", "implement-step");
        var nodeTest = MakeFunctionNode("test", "test-step");

        dag.AddNode(nodeImpl);
        dag.AddNode(nodeTest);
        dag.AddEdge(new DagEdge { Id = "e-impl-test", FromId = "implement", ToId = "test" });
        const string backEdgeId = "e-test-impl";
        dag.TryAddEdge(new DagEdge { Id = backEdgeId, FromId = "test", ToId = "implement", Label = "FAIL" });
        nodeImpl.InEdgeIds.Remove(backEdgeId);

        engine.RegisterFunction("implement", _ =>
        {
            implementCallCount++;
            return Task.FromResult(NodeResult.Succeeded($"implement-v{implementCallCount}", tokensUsed: 15));
        });

        // test 始终返回 FAIL，触发持续重试
        engine.RegisterFunction("test", _ =>
            Task.FromResult(NodeResult.Routed("test-fail", ["FAIL"], tokensUsed: 10)));

        var graph = new GoalGraph
        {
            Name = "retry-exceed-test",
            Dag = dag,
            StartNodeId = "implement",
            EndNodeIds = FrozenSet.Create("test"),
            MaxRetriesPerNode = 2, // 最多重试 2 次
        };

        var goalState = CreateGoalState();
        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);

        // implement 被执行 3 次（初始 + 重试 2 次），第 3 次重试时超过 MaxRetries
        Assert.Equal(3, implementCallCount);

        // implement 被标记为 Failed（超过最大重试次数）
        Assert.Equal(GoalNodeStatus.Failed, nodeImpl.Payload.Status);
        Assert.Contains("Max retries", nodeImpl.Payload.ErrorMessage);

        // implement 不是 End 节点，其 Failed 不直接导致 GoalStatus.Unmet
        // End 节点 test 已完成 → GoalStatus.Achieved
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 6. JoinNode：2 个并行节点完成后 Join 汇聚
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinNode_Should_WaitForAllUpstreams_AndMergeOutput()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "source-a");
        var nodeB = MakeFunctionNode("B", "source-b");
        var nodeJ = MakeJoinNode("J", "join-result");

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeJ);
        // A fan-out: A→B, A→C 结构改为 A 为 start，A→B, A→J, B→J
        // 实际：A→B（空Label），A→J（空Label），B→J（空Label）
        // 这样 A 完成后同时推进 B 和 J，但 J 需要等 B 也完成
        dag.AddEdge(new DagEdge { Id = "e-a-b", FromId = "A", ToId = "B" });
        dag.AddEdge(new DagEdge { Id = "e-a-j", FromId = "A", ToId = "J" });
        dag.AddEdge(new DagEdge { Id = "e-b-j", FromId = "B", ToId = "J" });

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Succeeded("output-A", tokensUsed: 10)));

        engine.RegisterFunction("B", _ =>
            Task.FromResult(NodeResult.Succeeded("output-B", tokensUsed: 20)));

        var graph = new GoalGraph
        {
            Name = "join-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
        };

        var goalState = CreateGoalState();
        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);

        Assert.Equal(GoalNodeStatus.Completed, nodeJ.Payload.Status);
        Assert.Equal(GoalStatus.Achieved, result.Status);

        // Join 输出应包含两个上游的输出
        Assert.Contains("A", nodeJ.Payload.Output);
        Assert.Contains("output-A", nodeJ.Payload.Output);
        Assert.Contains("B", nodeJ.Payload.Output);
        Assert.Contains("output-B", nodeJ.Payload.Output);
    }

    // ─────────────────────────────────────────────────────────────
    // 7. 节点超时：TimeoutSeconds=1 的节点执行超时 → 标记 Failed
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NodeTimeout_Should_MarkAsFailed_WhenExceedsTimeout()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = new DagNode<GoalNodePayload>
        {
            Id = "A",
            Payload = new GoalNodePayload
            {
                Kind = GoalNodeKind.Function,
                Name = "slow-node",
                TimeoutSeconds = 1, // 1 秒超时
            },
        };

        dag.AddNode(nodeA);

        // 注册一个延迟 5 秒的函数（会因超时被取消）
        engine.RegisterFunction("A", async ctx =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ctx.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 重新抛出，让引擎捕获超时
                throw;
            }

            return NodeResult.Succeeded("should-not-reach");
        });

        var graph = new GoalGraph
        {
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
    // 8. GoalState 更新：TokensUsed 和 TurnsCompleted 正确更新
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GoalStateUpdate_Should_AccumulateTokensAndTurns()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "step-a");
        var nodeB = MakeFunctionNode("B", "step-b");

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddEdge(new DagEdge { Id = "e-ab", FromId = "A", ToId = "B" });

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Succeeded("output-A", tokensUsed: 100)));

        engine.RegisterFunction("B", _ =>
            Task.FromResult(NodeResult.Succeeded("output-B", tokensUsed: 200)));

        var graph = new GoalGraph
        {
            Name = "state-update-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("B"),
        };

        var goalState = CreateGoalState();
        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);

        // TokensUsed = 100 + 200 = 300
        Assert.Equal(300, result.TokensUsed);

        // TurnsCompleted = 2（A 和 B 都完成）
        Assert.Equal(2, result.TurnsCompleted);

        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 补充：Function 未注册 → Output 为 null（NodeResult.Failed 是软失败，不抛异常）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnregisteredFunction_Should_MarkNodeAsFailed()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "missing-fn");

        dag.AddNode(nodeA);

        // 故意不注册 A 的函数

        var graph = new GoalGraph
        {
            Name = "unregistered-fn-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("A"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        // NodeResult.Failed → 节点标记 Failed
        Assert.Equal(GoalNodeStatus.Failed, nodeA.Payload.Status);
        Assert.Null(nodeA.Payload.Output);
        Assert.Contains("Function not registered", nodeA.Payload.ErrorMessage);
    }

    // ─────────────────────────────────────────────────────────────
    // 补充：JoinNode 前置条件不满足 → Output 为 null（软失败）
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinNode_WhenPreconditionNotMet_Should_MarkNodeAsFailed()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "source");
        // Join 要求 2 个上游成功，但只有 1 个上游
        var nodeJ = MakeJoinNode("J", "join", minSuccessfulInputs: 2);

        dag.AddNode(nodeA);
        dag.AddNode(nodeJ);
        dag.AddEdge(new DagEdge { Id = "e-a-j", FromId = "A", ToId = "J" });

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Succeeded("output-A")));

        var graph = new GoalGraph
        {
            Name = "join-precondition-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        // J 的 MinSuccessfulInputs=2 但只有 1 个上游完成 → NodeResult.Failed → 节点标记 Failed
        Assert.Equal(GoalNodeStatus.Failed, nodeJ.Payload.Status);
        Assert.Null(nodeJ.Payload.Output);
        Assert.Contains("Join precondition not met", nodeJ.Payload.ErrorMessage);
    }

    // ─────────────────────────────────────────────────────────────
    // 补充：RouteMatchMode.All — 空 Label + 匹配条件边都走
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RouteMatchModeAll_Should_FollowBothConditionalAndUnconditional()
    {
        var engine = CreateEngine();
        var executedNodes = new List<string>();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = new DagNode<GoalNodePayload>
        {
            Id = "A",
            Payload = new GoalNodePayload
            {
                Kind = GoalNodeKind.Function,
                Name = "fan-out-router",
                RouteMatchMode = RouteMatchMode.All,
            },
        };
        var nodeB = MakeFunctionNode("B", "conditional-handler");
        var nodeC = MakeFunctionNode("C", "unconditional-handler");

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeC);
        dag.AddEdge(new DagEdge { Id = "e-cond", FromId = "A", ToId = "B", Label = "PASS" });
        dag.AddEdge(new DagEdge { Id = "e-uncond", FromId = "A", ToId = "C" }); // 空 Label

        engine.RegisterFunction("A", _ =>
        {
            executedNodes.Add("A");
            return Task.FromResult(NodeResult.Routed("A-done", ["PASS"]));
        });

        engine.RegisterFunction("B", _ =>
        {
            executedNodes.Add("B");
            return Task.FromResult(NodeResult.Succeeded("B-done"));
        });

        engine.RegisterFunction("C", _ =>
        {
            executedNodes.Add("C");
            return Task.FromResult(NodeResult.Succeeded("C-done"));
        });

        var graph = new GoalGraph
        {
            Name = "route-all-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("B", "C"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        // All 模式下，PASS 边和空 Label 边都走
        Assert.Contains("B", executedNodes);
        Assert.Contains("C", executedNodes);
    }

    // ─────────────────────────────────────────────────────────────
    // 补充：取消令牌 — 执行中途取消应抛出 OperationCanceledException
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task CancellationMidExecution_Should_ThrowOperationCanceledException()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "slow-node");

        dag.AddNode(nodeA);

        using var cts = new CancellationTokenSource();

        engine.RegisterFunction("A", async ctx =>
        {
            // 延迟后取消
            await Task.Delay(100, CancellationToken.None);
            cts.Cancel();
            // 再延迟让取消传播
            await Task.Delay(100, ctx.CancellationToken);
            return NodeResult.Succeeded("done");
        });

        var graph = new GoalGraph
        {
            Name = "cancel-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("A"),
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), cts.Token));
    }

    // ─────────────────────────────────────────────────────────────
    // 补充：多 End 节点 — 所有 End 完成后才 Achieved
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task MultipleEndNodes_Should_AchieveOnlyWhenAllEndsComplete()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "source");
        var nodeB = MakeFunctionNode("B", "branch-b");
        var nodeC = MakeFunctionNode("C", "branch-c");

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeC);
        dag.AddEdge(new DagEdge { Id = "e-a-b", FromId = "A", ToId = "B" });
        dag.AddEdge(new DagEdge { Id = "e-a-c", FromId = "A", ToId = "C" });

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Succeeded("output-A")));
        engine.RegisterFunction("B", _ =>
            Task.FromResult(NodeResult.Succeeded("output-B")));
        engine.RegisterFunction("C", _ =>
            Task.FromResult(NodeResult.Succeeded("output-C")));

        var graph = new GoalGraph
        {
            Name = "multi-end-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("B", "C"), // B 和 C 都是 End
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalStatus.Achieved, result.Status);
        Assert.Equal(GoalNodeStatus.Completed, nodeB.Payload.Status);
        Assert.Equal(GoalNodeStatus.Completed, nodeC.Payload.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 补充：NodeContext.Services 注入 — FunctionNode 可通过 Services 获取 DI 服务
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task FunctionNode_Should_ReceiveServiceProvider_FromContext()
    {
        var services = new ServiceCollection();
        services.AddSingleton("test-value-from-di");
        var sp = services.BuildServiceProvider();

        var engine = CreateEngine(serviceProvider: sp);
        string? receivedValue = null;

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "fn-with-di");

        dag.AddNode(nodeA);

        engine.RegisterFunction("A", ctx =>
        {
            receivedValue = ctx.Services.GetService<string>();
            return Task.FromResult(NodeResult.Succeeded($"got: {receivedValue}"));
        });

        var graph = new GoalGraph
        {
            Name = "di-injection-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("A"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal("test-value-from-di", receivedValue);
        Assert.Equal(GoalNodeStatus.Completed, nodeA.Payload.Status);
        Assert.Equal("got: test-value-from-di", nodeA.Payload.Output);
    }

    // ─────────────────────────────────────────────────────────────
    // 补充：NodeResult.Failed → 节点标记 Failed + EndNode → GoalStatus.Unmet
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task FunctionNodeFailed_AsEndNode_Should_SetGoalUnmet()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "failing-fn");

        dag.AddNode(nodeA);

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Failed("intentional failure")));

        var graph = new GoalGraph
        {
            Name = "failed-end-test",
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
    // P5: JoinNode 部分失败 — 2个上游中1个失败，MinSuccessfulInputs=1 → Join成功
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinNode_PartialFailure_Should_SucceedWhenMinSuccessfulMet()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "source-a");
        var nodeB = MakeFunctionNode("B", "source-b");
        var nodeC = MakeFunctionNode("C", "source-c-failing");
        var nodeJ = MakeJoinNode("J", "join", minSuccessfulInputs: 1);

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
        engine.RegisterFunction("B", _ =>
            Task.FromResult(NodeResult.Succeeded("output-B", tokensUsed: 20)));
        engine.RegisterFunction("C", _ =>
            Task.FromResult(NodeResult.Failed("C-failed", tokensUsed: 5)));

        var graph = new GoalGraph
        {
            Name = "join-partial-failure-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        // C 失败，但 B 成功 → 1/2 成功 >= MinSuccessfulInputs(1) → Join 成功
        Assert.Equal(GoalNodeStatus.Failed, nodeC.Payload.Status);
        Assert.Equal(GoalNodeStatus.Completed, nodeJ.Payload.Status);
        Assert.Contains("output-B", nodeJ.Payload.Output);
        Assert.Contains("warning", nodeJ.Payload.Output); // 部分失败告警
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // P5: JoinNode 全部失败 — MinSuccessfulInputs不满足 → Join失败
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task JoinNode_AllUpstreamsFailed_Should_Fail()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "source-a-failing");
        var nodeB = MakeFunctionNode("B", "source-b-failing");
        var nodeJ = MakeJoinNode("J", "join", minSuccessfulInputs: 1);

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeJ);
        dag.AddEdge(new DagEdge { Id = "e-a-j", FromId = "A", ToId = "J" });
        dag.AddEdge(new DagEdge { Id = "e-b-j", FromId = "B", ToId = "J" });

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Failed("A-failed")));
        engine.RegisterFunction("B", _ =>
            Task.FromResult(NodeResult.Failed("B-failed")));

        // 需要一个 StartNode → 让 A 为 Start，A→B 也走通
        dag = new Dag<GoalNodePayload>();
        nodeA = MakeFunctionNode("A", "source-a-failing");
        nodeB = MakeFunctionNode("B", "source-b-failing");
        nodeJ = MakeJoinNode("J", "join", minSuccessfulInputs: 1);

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddNode(nodeJ);
        dag.AddEdge(new DagEdge { Id = "e-a-b", FromId = "A", ToId = "B" });
        dag.AddEdge(new DagEdge { Id = "e-a-j", FromId = "A", ToId = "J" });
        dag.AddEdge(new DagEdge { Id = "e-b-j", FromId = "B", ToId = "J" });

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Failed("A-failed")));
        engine.RegisterFunction("B", _ =>
            Task.FromResult(NodeResult.Failed("B-failed")));

        var graph = new GoalGraph
        {
            Name = "join-all-failed-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalNodeStatus.Failed, nodeA.Payload.Status);
        Assert.Equal(GoalNodeStatus.Failed, nodeB.Payload.Status);
        Assert.Equal(GoalNodeStatus.Failed, nodeJ.Payload.Status);
        Assert.Contains("Join precondition not met", nodeJ.Payload.ErrorMessage);
    }
}
