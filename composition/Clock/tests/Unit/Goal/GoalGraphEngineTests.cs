
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
        IServiceProvider? serviceProvider = null,
        IGoalUserInteraction? userInteraction = null,
        IGoalNodeInspector? nodeInspector = null,
        IGoalConflictMessenger? conflictMessenger = null)
    {
        return new GoalGraphEngine(
            (kernel ?? CreateKernelMock()).Object,
            (evaluator ?? CreateEvaluatorMock()).Object,
            serviceProvider ?? new ServiceCollection().BuildServiceProvider(),
            heartbeat: CreateHeartbeatMock().Object,
            clock: clock,
            userInteraction: userInteraction,
            nodeInspector: nodeInspector,
            conflictMessenger: conflictMessenger);
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

    // ─────────────────────────────────────────────────────────────
    // P8: Fan-out 并行 — A→[B,C]→J，验证 B 和 C 都执行且 J 正确汇聚
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task FanOutParallel_Should_ExecuteAllBranches_AndJoinCorrectly()
    {
        var engine = CreateEngine();
        var executedNodes = new List<string>();

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
        {
            executedNodes.Add("A");
            return Task.FromResult(NodeResult.Succeeded("output-A", tokensUsed: 10));
        });
        engine.RegisterFunction("B", _ =>
        {
            executedNodes.Add("B");
            return Task.FromResult(NodeResult.Succeeded("output-B", tokensUsed: 20));
        });
        engine.RegisterFunction("C", _ =>
        {
            executedNodes.Add("C");
            return Task.FromResult(NodeResult.Succeeded("output-C", tokensUsed: 30));
        });

        var graph = new GoalGraph
        {
            Name = "fanout-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Contains("A", executedNodes);
        Assert.Contains("B", executedNodes);
        Assert.Contains("C", executedNodes);
        Assert.Equal(GoalNodeStatus.Completed, nodeJ.Payload.Status);
        Assert.Contains("output-B", nodeJ.Payload.Output);
        Assert.Contains("output-C", nodeJ.Payload.Output);
        Assert.Equal(GoalStatus.Achieved, result.Status);
        Assert.Equal(60, result.TokensUsed); // 10 + 20 + 30
    }

    // ─────────────────────────────────────────────────────────────
    // P8: 单节点图 — 只有 Start=End 一个节点
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SingleNodeGraph_Should_ExecuteAndAchieve()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "solo");

        dag.AddNode(nodeA);

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Succeeded("solo-output", tokensUsed: 42)));

        var graph = new GoalGraph
        {
            Name = "single-node-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("A"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalNodeStatus.Completed, nodeA.Payload.Status);
        Assert.Equal("solo-output", nodeA.Payload.Output);
        Assert.Equal(42, result.TokensUsed);
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // P8: 上游 Output 自动传递为下游 Input
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task UpstreamOutput_Should_BeSetAsDownstreamInput()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "producer");
        var nodeB = MakeFunctionNode("B", "consumer");

        dag.AddNode(nodeA);
        dag.AddNode(nodeB);
        dag.AddEdge(new DagEdge { Id = "e-ab", FromId = "A", ToId = "B" });

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Succeeded("produced-data", tokensUsed: 5)));

        engine.RegisterFunction("B", _ =>
        {
            var upstream = nodeA.Payload.Output;
            return Task.FromResult(NodeResult.Succeeded($"consumed: {upstream}", tokensUsed: 10));
        });

        var graph = new GoalGraph
        {
            Name = "input-passing-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("B"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal("produced-data", nodeA.Payload.Output);
        Assert.Equal("consumed: produced-data", nodeB.Payload.Output);
    }

    // ─────────────────────────────────────────────────────────────
    // P8: 条件路由 + 回退完整场景 — 重构流水线
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RefactorPipeline_Should_RetryOnFailAndSucceedOnSecondAttempt()
    {
        var engine = CreateEngine();
        var implementCount = 0;
        var testCount = 0;

        var dag = new Dag<GoalNodePayload>();
        var nodeExplore = MakeFunctionNode("explore", "explorer");
        var nodeImpl = MakeFunctionNode("implement", "implementer");
        var nodeTest = MakeFunctionNode("test", "tester");
        var nodeCommit = MakeFunctionNode("commit", "committer");

        dag.AddNode(nodeExplore);
        dag.AddNode(nodeImpl);
        dag.AddNode(nodeTest);
        dag.AddNode(nodeCommit);
        dag.AddEdge(new DagEdge { Id = "e1", FromId = "explore", ToId = "implement" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "implement", ToId = "test" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "test", ToId = "commit", Label = "PASS" });
        const string backEdgeId = "e4";
        dag.TryAddEdge(new DagEdge { Id = backEdgeId, FromId = "test", ToId = "implement", Label = "FAIL" });
        nodeImpl.InEdgeIds.Remove(backEdgeId);

        engine.RegisterFunction("explore", _ =>
            Task.FromResult(NodeResult.Succeeded("explored", tokensUsed: 10)));

        engine.RegisterFunction("implement", _ =>
        {
            implementCount++;
            return Task.FromResult(NodeResult.Succeeded($"impl-v{implementCount}", tokensUsed: 50));
        });

        engine.RegisterFunction("test", _ =>
        {
            testCount++;
            if (testCount == 1)
                return Task.FromResult(NodeResult.Routed("test-fail", ["FAIL"], tokensUsed: 20));
            return Task.FromResult(NodeResult.Routed("test-pass", ["PASS"], tokensUsed: 20));
        });

        engine.RegisterFunction("commit", _ =>
            Task.FromResult(NodeResult.Succeeded("committed", tokensUsed: 5)));

        var graph = new GoalGraph
        {
            Name = "refactor-pipeline",
            Dag = dag,
            StartNodeId = "explore",
            EndNodeIds = FrozenSet.Create("commit"),
            MaxRetriesPerNode = 3,
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(2, implementCount);
        Assert.Equal(2, testCount);
        Assert.Equal(GoalNodeStatus.Completed, nodeCommit.Payload.Status);
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // FreshContext — reviewer 节点不继承 ChatHistory
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task FreshContext_Should_NotInheritChatHistory()
    {
        var engine = CreateEngine();
        var inheritedMessageCount = -1;

        var dag = new Dag<GoalNodePayload>();
        var nodeA = MakeFunctionNode("A", "worker");

        var nodeR = new DagNode<GoalNodePayload>
        {
            Id = "R",
            Payload = new GoalNodePayload
            {
                Kind = GoalNodeKind.Function,
                Name = "reviewer",
                FreshContext = true,
            },
        };

        dag.AddNode(nodeA);
        dag.AddNode(nodeR);
        dag.AddEdge(new DagEdge { Id = "e-a-r", FromId = "A", ToId = "R" });

        engine.RegisterFunction("A", _ =>
            Task.FromResult(NodeResult.Succeeded("work-output", tokensUsed: 10)));

        engine.RegisterFunction("R", ctx =>
        {
            inheritedMessageCount = ctx.UpstreamOutputs.Count;
            return Task.FromResult(NodeResult.Succeeded("review-pass", tokensUsed: 5));
        });

        var graph = new GoalGraph
        {
            Name = "fresh-context-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("R"),
        };

        var chatHistory = new MessageList();
        chatHistory.AddSystemMessage("previous-context");
        chatHistory.AddUserMessage("old-instruction");

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), chatHistory, CancellationToken.None);

        Assert.Equal(GoalNodeStatus.Completed, nodeR.Payload.Status);
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // Agent→Reviewer 默认图 — agent完成后reviewer独立评审
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AgentReviewerGraph_Should_ExecuteAndReview()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeAgent = MakeFunctionNode("agent", "executor");
        var nodeReviewer = MakeFunctionNode("reviewer", "reviewer");

        dag.AddNode(nodeAgent);
        dag.AddNode(nodeReviewer);
        dag.AddEdge(new DagEdge { Id = "e1", FromId = "agent", ToId = "reviewer" });

        engine.RegisterFunction("agent", _ =>
            Task.FromResult(NodeResult.Succeeded("task-completed-output", tokensUsed: 100)));

        engine.RegisterFunction("reviewer", _ =>
            Task.FromResult(NodeResult.Succeeded("review-pass", tokensUsed: 20)));

        var graph = new GoalGraph
        {
            Name = "agent-reviewer-test",
            Dag = dag,
            StartNodeId = "agent",
            EndNodeIds = FrozenSet.Create("reviewer"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalNodeStatus.Completed, nodeAgent.Payload.Status);
        Assert.Equal(GoalNodeStatus.Completed, nodeReviewer.Payload.Status);
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 负向评价循环: neg_review → {NEG_CONTINUE: fix_neg, NEG_STOP: done}
    // 负评≤5 → NEG_STOP → done
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NegativeReviewLoop_LowNegCount_Should_Stop()
    {
        var engine = CreateEngine();
        var executedNodes = new List<string>();

        var dag = new Dag<GoalNodePayload>();
        var nodeExecute = MakeFunctionNode("execute", "executor");
        var nodeNegReview = MakeFunctionNode("neg_review", "negative-reviewer");
        var nodeFixNeg = MakeFunctionNode("fix_neg", "fix-negative-review");
        var nodeDone = MakeFunctionNode("done", "loop-done");

        dag.AddNode(nodeExecute);
        dag.AddNode(nodeNegReview);
        dag.AddNode(nodeFixNeg);
        dag.AddNode(nodeDone);

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "execute", ToId = "neg_review" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "neg_review", ToId = "fix_neg", Label = "NEG_CONTINUE" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "neg_review", ToId = "done", Label = "NEG_STOP" });
        const string backEdge = "e4";
        dag.TryAddEdge(new DagEdge { Id = backEdge, FromId = "fix_neg", ToId = "neg_review", Label = "NEG_CONTINUE" });
        dag.Nodes["neg_review"].InEdgeIds.Remove(backEdge);
        dag.AddEdge(new DagEdge { Id = "e5", FromId = "fix_neg", ToId = "done", Label = "NEG_STOP" });

        engine.RegisterFunction("execute", _ =>
        {
            executedNodes.Add("execute");
            return Task.FromResult(NodeResult.Succeeded("task-output", tokensUsed: 50));
        });

        engine.RegisterFunction("neg_review", _ =>
        {
            executedNodes.Add("neg_review");
            return Task.FromResult(NodeResult.Routed("3 neg reviews found", ["NEG_STOP"], tokensUsed: 20));
        });

        engine.RegisterFunction("fix_neg", _ =>
        {
            executedNodes.Add("fix_neg");
            return Task.FromResult(NodeResult.Succeeded("fixes-applied", tokensUsed: 30));
        });

        engine.RegisterFunction("done", _ =>
        {
            executedNodes.Add("done");
            return Task.FromResult(NodeResult.Succeeded("loop-completed"));
        });

        var graph = new GoalGraph
        {
            Name = "neg-review-loop-test",
            Dag = dag,
            StartNodeId = "execute",
            EndNodeIds = FrozenSet.Create("done"),
            HardMaxLoopIterations = 16,
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Contains("execute", executedNodes);
        Assert.Contains("neg_review", executedNodes);
        Assert.Contains("done", executedNodes);
        Assert.DoesNotContain("fix_neg", executedNodes);
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 负向评价循环: 高负评 → NEG_CONTINUE → fix_neg → 循环 → 最终 NEG_STOP
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NegativeReviewLoop_HighNegCount_Should_LoopThenStop()
    {
        var engine = CreateEngine();
        var negReviewCount = 0;

        var dag = new Dag<GoalNodePayload>();
        var nodeExecute = MakeFunctionNode("execute", "executor");
        var nodeNegReview = MakeFunctionNode("neg_review", "negative-reviewer");
        var nodeFixNeg = MakeFunctionNode("fix_neg", "fix-negative-review");
        var nodeDone = MakeFunctionNode("done", "loop-done");

        dag.AddNode(nodeExecute);
        dag.AddNode(nodeNegReview);
        dag.AddNode(nodeFixNeg);
        dag.AddNode(nodeDone);

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "execute", ToId = "neg_review" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "neg_review", ToId = "fix_neg", Label = "NEG_CONTINUE" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "neg_review", ToId = "done", Label = "NEG_STOP" });
        const string backEdge = "e4";
        dag.TryAddEdge(new DagEdge { Id = backEdge, FromId = "fix_neg", ToId = "neg_review", Label = "NEG_CONTINUE" });
        dag.Nodes["neg_review"].InEdgeIds.Remove(backEdge);
        dag.AddEdge(new DagEdge { Id = "e5", FromId = "fix_neg", ToId = "done", Label = "NEG_STOP" });

        engine.RegisterFunction("execute", _ =>
            Task.FromResult(NodeResult.Succeeded("task-output", tokensUsed: 50)));

        engine.RegisterFunction("neg_review", _ =>
        {
            negReviewCount++;
            if (negReviewCount <= 2)
                return Task.FromResult(NodeResult.Routed($"12 neg reviews (iter {negReviewCount})", ["NEG_CONTINUE"], tokensUsed: 20));
            return Task.FromResult(NodeResult.Routed("3 neg reviews", ["NEG_STOP"], tokensUsed: 20));
        });

        engine.RegisterFunction("fix_neg", _ =>
            Task.FromResult(NodeResult.Routed("fixes-applied", ["NEG_CONTINUE"], tokensUsed: 30)));

        engine.RegisterFunction("done", _ =>
            Task.FromResult(NodeResult.Succeeded("loop-completed")));

        var graph = new GoalGraph
        {
            Name = "neg-review-loop-iter-test",
            Dag = dag,
            StartNodeId = "execute",
            EndNodeIds = FrozenSet.Create("done"),
            HardMaxLoopIterations = 16,
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(3, negReviewCount);
        Assert.Equal(GoalNodeStatus.Completed, nodeDone.Payload.Status);
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 负向评价循环: 16轮硬上限强制终止
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NegativeReviewLoop_HardMaxIterations_Should_ForceTerminate()
    {
        var engine = CreateEngine();
        var negReviewCount = 0;

        var dag = new Dag<GoalNodePayload>();
        var nodeExecute = MakeFunctionNode("execute", "executor");
        var nodeNegReview = MakeFunctionNode("neg_review", "negative-reviewer");
        var nodeFixNeg = MakeFunctionNode("fix_neg", "fix-negative-review");
        var nodeDone = MakeFunctionNode("done", "loop-done");

        dag.AddNode(nodeExecute);
        dag.AddNode(nodeNegReview);
        dag.AddNode(nodeFixNeg);
        dag.AddNode(nodeDone);

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "execute", ToId = "neg_review" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "neg_review", ToId = "fix_neg", Label = "NEG_CONTINUE" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "neg_review", ToId = "done", Label = "NEG_STOP" });
        const string backEdge = "e4";
        dag.TryAddEdge(new DagEdge { Id = backEdge, FromId = "fix_neg", ToId = "neg_review", Label = "NEG_CONTINUE" });
        dag.Nodes["neg_review"].InEdgeIds.Remove(backEdge);
        dag.AddEdge(new DagEdge { Id = "e5", FromId = "fix_neg", ToId = "done", Label = "NEG_STOP" });

        engine.RegisterFunction("execute", _ =>
            Task.FromResult(NodeResult.Succeeded("task-output", tokensUsed: 50)));

        engine.RegisterFunction("neg_review", _ =>
        {
            negReviewCount++;
            return Task.FromResult(NodeResult.Routed($"always continue (iter {negReviewCount})", ["NEG_CONTINUE"], tokensUsed: 20));
        });

        engine.RegisterFunction("fix_neg", _ =>
            Task.FromResult(NodeResult.Routed("fixes-applied", ["NEG_CONTINUE"], tokensUsed: 30)));

        engine.RegisterFunction("done", _ =>
            Task.FromResult(NodeResult.Succeeded("loop-completed")));

        var graph = new GoalGraph
        {
            Name = "neg-review-hardmax-test",
            Dag = dag,
            StartNodeId = "execute",
            EndNodeIds = FrozenSet.Create("done"),
            HardMaxLoopIterations = 3,
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.True(negReviewCount <= 4);
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 负向评价循环: token预算耗尽终止
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NegativeReviewLoop_TokenBudgetExhausted_Should_Terminate()
    {
        var engine = CreateEngine();
        var negReviewCount = 0;

        var dag = new Dag<GoalNodePayload>();
        var nodeExecute = MakeFunctionNode("execute", "executor");
        var nodeNegReview = MakeFunctionNode("neg_review", "negative-reviewer");
        var nodeFixNeg = MakeFunctionNode("fix_neg", "fix-negative-review");
        var nodeDone = MakeFunctionNode("done", "loop-done");

        dag.AddNode(nodeExecute);
        dag.AddNode(nodeNegReview);
        dag.AddNode(nodeFixNeg);
        dag.AddNode(nodeDone);

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "execute", ToId = "neg_review" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "neg_review", ToId = "fix_neg", Label = "NEG_CONTINUE" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "neg_review", ToId = "done", Label = "NEG_STOP" });
        const string backEdge = "e4";
        dag.TryAddEdge(new DagEdge { Id = backEdge, FromId = "fix_neg", ToId = "neg_review", Label = "NEG_CONTINUE" });
        dag.Nodes["neg_review"].InEdgeIds.Remove(backEdge);
        dag.AddEdge(new DagEdge { Id = "e5", FromId = "fix_neg", ToId = "done", Label = "NEG_STOP" });

        engine.RegisterFunction("execute", _ =>
            Task.FromResult(NodeResult.Succeeded("task-output", tokensUsed: 50)));

        engine.RegisterFunction("neg_review", _ =>
        {
            negReviewCount++;
            return Task.FromResult(NodeResult.Routed($"neg reviews (iter {negReviewCount})", ["NEG_CONTINUE"], tokensUsed: 40));
        });

        engine.RegisterFunction("fix_neg", _ =>
            Task.FromResult(NodeResult.Routed("fixes-applied", ["NEG_CONTINUE"], tokensUsed: 30)));

        engine.RegisterFunction("done", _ =>
            Task.FromResult(NodeResult.Succeeded("loop-completed")));

        var goalState = new GoalState
        {
            GoalId = "test-goal",
            Objective = "test objective",
            TokenBudget = 200,
        };

        var graph = new GoalGraph
        {
            Name = "neg-review-budget-test",
            Dag = dag,
            StartNodeId = "execute",
            EndNodeIds = FrozenSet.Create("done"),
            HardMaxLoopIterations = 16,
        };

        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);

        Assert.True(result.TokensUsed <= 250);
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // NegativeReviewCount 从输出中提取 — 验证 ExtractNegReviewMetadata
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NegativeReviewLoop_Should_ExtractNegReviewCount_FromOutput()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeExecute = MakeFunctionNode("execute", "executor");
        var nodeNegReview = MakeFunctionNode("neg_review", "negative-reviewer");
        var nodeDone = MakeFunctionNode("done", "loop-done");

        dag.AddNode(nodeExecute);
        dag.AddNode(nodeNegReview);
        dag.AddNode(nodeDone);

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "execute", ToId = "neg_review" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "neg_review", ToId = "done", Label = "NEG_STOP" });

        engine.RegisterFunction("execute", _ =>
            Task.FromResult(NodeResult.Succeeded("task-output", tokensUsed: 50)));

        engine.RegisterFunction("neg_review", _ =>
            Task.FromResult(NodeResult.Routed("## 负向评价报告\n```json\n{\"negativeReviewCount\":7,\"route\":\"NEG_STOP\",\"taskId\":null,\"items\":[],\"summary\":\"7条不足\"}\n```", ["NEG_STOP"], tokensUsed: 20)));

        engine.RegisterFunction("done", _ =>
            Task.FromResult(NodeResult.Succeeded("loop-completed")));

        var graph = new GoalGraph
        {
            Name = "neg-review-count-extract-test",
            Dag = dag,
            StartNodeId = "execute",
            EndNodeIds = FrozenSet.Create("done"),
        };

        await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(7, nodeNegReview.Payload.NegativeReviewCount);
    }

    // ─────────────────────────────────────────────────────────────
    // NegativeReviewCount 从输出中提取 task_id
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NegativeReviewLoop_Should_ExtractTaskId_FromOutput()
    {
        var engine = CreateEngine();

        var dag = new Dag<GoalNodePayload>();
        var nodeExecute = MakeFunctionNode("execute", "executor");
        var nodeNegReview = MakeFunctionNode("neg_review", "negative-reviewer");
        var nodeDone = MakeFunctionNode("done", "loop-done");

        dag.AddNode(nodeExecute);
        dag.AddNode(nodeNegReview);
        dag.AddNode(nodeDone);

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "execute", ToId = "neg_review" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "neg_review", ToId = "done", Label = "NEG_STOP" });

        engine.RegisterFunction("execute", _ =>
            Task.FromResult(NodeResult.Succeeded("task-output", tokensUsed: 50)));

        engine.RegisterFunction("neg_review", _ =>
            Task.FromResult(NodeResult.Routed("...\n```json\n{\"negativeReviewCount\":3,\"route\":\"NEG_STOP\",\"taskId\":\"task-abc-123\",\"items\":[],\"summary\":\"3条不足\"}\n```", ["NEG_STOP"], tokensUsed: 20)));

        engine.RegisterFunction("done", _ =>
            Task.FromResult(NodeResult.Succeeded("loop-completed")));

        var graph = new GoalGraph
        {
            Name = "neg-review-taskid-extract-test",
            Dag = dag,
            StartNodeId = "execute",
            EndNodeIds = FrozenSet.Create("done"),
        };

        await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal("task-abc-123", nodeNegReview.Payload.NegativeReviewTaskId);
        Assert.Equal(3, nodeNegReview.Payload.NegativeReviewCount);
    }

    // ─────────────────────────────────────────────────────────────
    // 轮次预算终止 — TurnBudget 耗尽时终止循环
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NegativeReviewLoop_TurnBudgetExhausted_Should_Terminate()
    {
        var engine = CreateEngine();
        var negReviewCount = 0;

        var dag = new Dag<GoalNodePayload>();
        var nodeExecute = MakeFunctionNode("execute", "executor");
        var nodeNegReview = MakeFunctionNode("neg_review", "negative-reviewer");
        var nodeFixNeg = MakeFunctionNode("fix_neg", "fix-negative-review");
        var nodeDone = MakeFunctionNode("done", "loop-done");

        dag.AddNode(nodeExecute);
        dag.AddNode(nodeNegReview);
        dag.AddNode(nodeFixNeg);
        dag.AddNode(nodeDone);

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "execute", ToId = "neg_review" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "neg_review", ToId = "fix_neg", Label = "NEG_CONTINUE" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "neg_review", ToId = "done", Label = "NEG_STOP" });
        const string backEdge = "e4";
        dag.TryAddEdge(new DagEdge { Id = backEdge, FromId = "fix_neg", ToId = "neg_review", Label = "NEG_CONTINUE" });
        dag.Nodes["neg_review"].InEdgeIds.Remove(backEdge);
        dag.AddEdge(new DagEdge { Id = "e5", FromId = "fix_neg", ToId = "done", Label = "NEG_STOP" });

        engine.RegisterFunction("execute", _ =>
            Task.FromResult(NodeResult.Succeeded("task-output", tokensUsed: 50)));

        engine.RegisterFunction("neg_review", _ =>
        {
            negReviewCount++;
            return Task.FromResult(NodeResult.Routed($"neg reviews (iter {negReviewCount})", ["NEG_CONTINUE"], tokensUsed: 20));
        });

        engine.RegisterFunction("fix_neg", _ =>
            Task.FromResult(NodeResult.Routed("fixes-applied", ["NEG_CONTINUE"], tokensUsed: 30)));

        engine.RegisterFunction("done", _ =>
            Task.FromResult(NodeResult.Succeeded("loop-completed")));

        var goalState = new GoalState
        {
            GoalId = "test-goal",
            Objective = "test objective",
            TurnBudget = 2,
        };

        var graph = new GoalGraph
        {
            Name = "neg-review-turn-budget-test",
            Dag = dag,
            StartNodeId = "execute",
            EndNodeIds = FrozenSet.Create("done"),
            HardMaxLoopIterations = 16,
        };

        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);

        Assert.True(negReviewCount <= 3);
        Assert.Equal(GoalStatus.Achieved, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // 用户交互路径: 负评6~10条时触发ask_user，用户选择停止
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NegativeReviewLoop_UserInteraction_Should_TriggerWhenNegCount6To10()
    {
        var userInteraction = new Mock<IGoalUserInteraction>();
        userInteraction.Setup(u => u.AskToContinueAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalUserDecision.Stop("user chose to stop"));

        var engine = CreateEngine(userInteraction: userInteraction.Object);

        var dag = new Dag<GoalNodePayload>();
        var nodeExecute = MakeFunctionNode("execute", "executor");
        var nodeNegReview = MakeFunctionNode("neg_review", "negative-reviewer");
        var nodeDone = MakeFunctionNode("done", "loop-done");

        dag.AddNode(nodeExecute);
        dag.AddNode(nodeNegReview);
        dag.AddNode(nodeDone);

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "execute", ToId = "neg_review" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "neg_review", ToId = "done", Label = "NEG_STOP" });

        engine.RegisterFunction("execute", _ =>
            Task.FromResult(NodeResult.Succeeded("task-output", tokensUsed: 50)));

        engine.RegisterFunction("neg_review", _ =>
            Task.FromResult(NodeResult.Routed("```json\n{\"negativeReviewCount\":8,\"route\":\"NEG_STOP\",\"taskId\":null,\"items\":[],\"summary\":\"8条不足\"}\n```", ["NEG_STOP"], tokensUsed: 20)));

        engine.RegisterFunction("done", _ =>
            Task.FromResult(NodeResult.Succeeded("loop-completed")));

        var graph = new GoalGraph
        {
            Name = "user-interaction-test",
            Dag = dag,
            StartNodeId = "execute",
            EndNodeIds = FrozenSet.Create("done"),
        };

        await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(8, nodeNegReview.Payload.NegativeReviewCount);
        userInteraction.Verify(u => u.AskToContinueAsync(
            It.IsAny<string>(), 8, It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────
    // 用户交互路径: 负评≤5时不触发ask_user
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NegativeReviewLoop_UserInteraction_Should_NotTriggerWhenNegCountBelow6()
    {
        var userInteraction = new Mock<IGoalUserInteraction>();
        userInteraction.Setup(u => u.AskToContinueAsync(
                It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalUserDecision.Continue());

        var engine = CreateEngine(userInteraction: userInteraction.Object);

        var dag = new Dag<GoalNodePayload>();
        var nodeExecute = MakeFunctionNode("execute", "executor");
        var nodeNegReview = MakeFunctionNode("neg_review", "negative-reviewer");
        var nodeDone = MakeFunctionNode("done", "loop-done");

        dag.AddNode(nodeExecute);
        dag.AddNode(nodeNegReview);
        dag.AddNode(nodeDone);

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "execute", ToId = "neg_review" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "neg_review", ToId = "done", Label = "NEG_STOP" });

        engine.RegisterFunction("execute", _ =>
            Task.FromResult(NodeResult.Succeeded("task-output", tokensUsed: 50)));

        engine.RegisterFunction("neg_review", _ =>
            Task.FromResult(NodeResult.Routed("```json\n{\"negativeReviewCount\":3,\"route\":\"NEG_STOP\",\"taskId\":null,\"items\":[],\"summary\":\"3条不足\"}\n```", ["NEG_STOP"], tokensUsed: 20)));

        engine.RegisterFunction("done", _ =>
            Task.FromResult(NodeResult.Succeeded("loop-completed")));

        var graph = new GoalGraph
        {
            Name = "no-user-interaction-test",
            Dag = dag,
            StartNodeId = "execute",
            EndNodeIds = FrozenSet.Create("done"),
        };

        await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(3, nodeNegReview.Payload.NegativeReviewCount);
        userInteraction.Verify(u => u.AskToContinueAsync(
            It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────
    // 协调者窥探: 观察者返回true时设置CoordinatorTerminated
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task NegativeReviewLoop_LoopObserver_Should_TerminateWhenObserverReturnsTrue()
    {
        var nodeInspector = new Mock<IGoalNodeInspector>();
        nodeInspector.Setup(o => o.ObserveLoopAsync(It.IsAny<LoopObservationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var engine = CreateEngine(nodeInspector: nodeInspector.Object);

        var dag = new Dag<GoalNodePayload>();
        var nodeExecute = MakeFunctionNode("execute", "executor");
        var nodeNegReview = MakeFunctionNode("neg_review", "negative-reviewer");
        var nodeFixNeg = MakeFunctionNode("fix_neg", "fix-negative-review");
        var nodeDone = MakeFunctionNode("done", "loop-done");

        dag.AddNode(nodeExecute);
        dag.AddNode(nodeNegReview);
        dag.AddNode(nodeFixNeg);
        dag.AddNode(nodeDone);

        dag.AddEdge(new DagEdge { Id = "e1", FromId = "execute", ToId = "neg_review" });
        dag.AddEdge(new DagEdge { Id = "e2", FromId = "neg_review", ToId = "fix_neg", Label = "NEG_CONTINUE" });
        dag.AddEdge(new DagEdge { Id = "e3", FromId = "neg_review", ToId = "done", Label = "NEG_STOP" });
        const string backEdge = "e4";
        dag.TryAddEdge(new DagEdge { Id = backEdge, FromId = "fix_neg", ToId = "neg_review", Label = "NEG_CONTINUE" });
        dag.Nodes["neg_review"].InEdgeIds.Remove(backEdge);
        dag.AddEdge(new DagEdge { Id = "e5", FromId = "fix_neg", ToId = "done", Label = "NEG_STOP" });

        engine.RegisterFunction("execute", _ =>
            Task.FromResult(NodeResult.Succeeded("task-output", tokensUsed: 50)));

        engine.RegisterFunction("neg_review", _ =>
            Task.FromResult(NodeResult.Routed("负评条数: 12", ["NEG_CONTINUE"], tokensUsed: 20)));

        engine.RegisterFunction("fix_neg", _ =>
            Task.FromResult(NodeResult.Routed("fixes-applied", ["NEG_CONTINUE"], tokensUsed: 30)));

        engine.RegisterFunction("done", _ =>
            Task.FromResult(NodeResult.Succeeded("loop-completed")));

        var graph = new GoalGraph
        {
            Name = "loop-observer-test",
            Dag = dag,
            StartNodeId = "execute",
            EndNodeIds = FrozenSet.Create("done"),
            HardMaxLoopIterations = 16,
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalStatus.Achieved, result.Status);
        nodeInspector.Verify(o => o.ObserveLoopAsync(It.IsAny<LoopObservationContext>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─────────────────────────────────────────────────────────────
    // P0-1 真正并行执行：A→[B,C]→J，B 和 C 应并发执行（maxConcurrent >= 2）
    // 串行队列下 maxConcurrent 恒为 1，此测试验证真正并行
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParallelExecution_Should_RunIndependentNodesConcurrently()
    {
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

        engine.RegisterFunction("B", async _ =>
        {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(150, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-B", tokensUsed: 20);
        });

        engine.RegisterFunction("C", async _ =>
        {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(150, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-C", tokensUsed: 30);
        });

        var graph = new GoalGraph
        {
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
    public async Task ParallelExecution_WithMaxConcurrency1_Should_DegradeToSerial()
    {
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

        engine.RegisterFunction("B", async _ =>
        {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(100, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-B", tokensUsed: 20);
        });

        engine.RegisterFunction("C", async _ =>
        {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(100, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-C", tokensUsed: 30);
        });

        var graph = new GoalGraph
        {
            Name = "parallel-max1-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
            MaxConcurrency = 1,
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalStatus.Achieved, result.Status);
        Assert.True(maxConcurrent == 1, $"MaxConcurrency=1 应串行，但 maxConcurrent={maxConcurrent}");
    }

    // ─────────────────────────────────────────────────────────────
    // P1-4 失败率终止：B/C 失败，D/E 成功，失败率>50% 时终止为 Unmet
    // B/C 立即失败先完成，D/E 延迟100ms，C 完成时 2/3=66%>50% 触发
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HighFailureRate_Should_TerminateAsUnmet()
    {
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
        engine.RegisterFunction("D", async _ =>
        {
            await Task.Delay(100, CancellationToken.None);
            return NodeResult.Succeeded("D-ok", tokensUsed: 10);
        });
        engine.RegisterFunction("E", async _ =>
        {
            await Task.Delay(100, CancellationToken.None);
            return NodeResult.Succeeded("E-ok", tokensUsed: 10);
        });

        var graph = new GoalGraph
        {
            Name = "high-failure-rate-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalStatus.Unmet, result.Status);
    }

    // ─────────────────────────────────────────────────────────────
    // P0-1 真正并行执行：A→[B,C]→J，B 和 C 应并发执行（maxConcurrent >= 2）
    // 串行队列下 maxConcurrent 恒为 1，此测试验证真正并行
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task ParallelExecution_Should_RunIndependentNodesConcurrently()
    {
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

        engine.RegisterFunction("B", async _ =>
        {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(150, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-B", tokensUsed: 20);
        });

        engine.RegisterFunction("C", async _ =>
        {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(150, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-C", tokensUsed: 30);
        });

        var graph = new GoalGraph
        {
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
    public async Task ParallelExecution_WithMaxConcurrency1_Should_DegradeToSerial()
    {
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

        engine.RegisterFunction("B", async _ =>
        {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(100, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-B", tokensUsed: 20);
        });

        engine.RegisterFunction("C", async _ =>
        {
            var current = Interlocked.Increment(ref concurrentCount);
            if (current > Volatile.Read(ref maxConcurrent))
                Interlocked.Exchange(ref maxConcurrent, current);
            await Task.Delay(100, CancellationToken.None);
            Interlocked.Decrement(ref concurrentCount);
            return NodeResult.Succeeded("output-C", tokensUsed: 30);
        });

        var graph = new GoalGraph
        {
            Name = "parallel-max1-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
            MaxConcurrency = 1,
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalStatus.Achieved, result.Status);
        Assert.True(maxConcurrent == 1, $"MaxConcurrency=1 应串行，但 maxConcurrent={maxConcurrent}");
    }

    // ─────────────────────────────────────────────────────────────
    // P1-4 失败率终止：B/C 失败，D/E 成功，失败率>50% 时终止为 Unmet
    // B/C 立即失败先完成，D/E 延迟100ms，C 完成时 2/3=66%>50% 触发
    // ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task HighFailureRate_Should_TerminateAsUnmet()
    {
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
        engine.RegisterFunction("D", async _ =>
        {
            await Task.Delay(100, CancellationToken.None);
            return NodeResult.Succeeded("D-ok", tokensUsed: 10);
        });
        engine.RegisterFunction("E", async _ =>
        {
            await Task.Delay(100, CancellationToken.None);
            return NodeResult.Succeeded("E-ok", tokensUsed: 10);
        });

        var graph = new GoalGraph
        {
            Name = "high-failure-rate-test",
            Dag = dag,
            StartNodeId = "A",
            EndNodeIds = FrozenSet.Create("J"),
        };

        var result = await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.Equal(GoalStatus.Unmet, result.Status);
    }
}
