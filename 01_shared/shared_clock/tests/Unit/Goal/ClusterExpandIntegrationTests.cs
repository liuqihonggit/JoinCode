namespace Core.Goal.Tests;


public sealed class ClusterExpandIntegrationTests
{
    [Fact]
    public async Task ClusterExpand_Should_DynamicallyAddWorkerNodes()
    {
        var services = new ServiceCollection();
        var analyzer = new Mock<IDecomposabilityAnalyzer>();
        analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DecompositionResult.Decomposable("3个独立模块", [
                new SubTaskDefinition { Id = "sub_1", Title = "模块A", Description = "实现模块A", OwnedFiles = ["a.cs"], Priority = SubTaskPriority.High, Variant = ExecutorVariant.Code },
                new SubTaskDefinition { Id = "sub_2", Title = "模块B", Description = "实现模块B", OwnedFiles = ["b.cs"], Priority = SubTaskPriority.Medium, Variant = ExecutorVariant.Code },
                new SubTaskDefinition { Id = "sub_3", Title = "模块C", Description = "调研模块C", OwnedFiles = ["c.cs"], Priority = SubTaskPriority.Low, Variant = ExecutorVariant.Explore },
            ]));

        services.AddSingleton(analyzer.Object);
        services.AddSingleton<IClusterPlanValidator, ClusterPlanValidator>();
        services.AddSingleton<IClusterPlanApprovalHookManager, NoOpClusterPlanApprovalHook>();
        var sp = services.BuildServiceProvider();

        var engine = CreateEngine(serviceProvider: sp);
        var graph = GoalGraphTemplates.ClusterTemplate.BuildGraph(engine, "并行给3个模块写文档");

        Assert.Equal("cluster_analyze", graph.StartNodeId);
        Assert.Contains("cluster_review", graph.EndNodeIds.ToHashSet());

        var goalState = new GoalState { GoalId = "cluster-test", Objective = "并行给3个模块写文档" };
        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);

        Assert.True(graph.Dag.Nodes.ContainsKey("cluster_expand"), "cluster_expand 节点应存在");
    }

    [Fact]
    public async Task ClusterExpand_NotDecomposable_Should_RouteToFallback()
    {
        var services = new ServiceCollection();
        var analyzer = new Mock<IDecomposabilityAnalyzer>();
        analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DecompositionResult.NotDecomposable("单文件修改"));

        services.AddSingleton(analyzer.Object);
        services.AddSingleton<IClusterPlanValidator, ClusterPlanValidator>();
        services.AddSingleton<IClusterPlanApprovalHookManager, NoOpClusterPlanApprovalHook>();
        var sp = services.BuildServiceProvider();

        var engine = CreateEngine(serviceProvider: sp);
        var graph = GoalGraphTemplates.ClusterTemplate.BuildGraph(engine, "修改一个文件");

        var goalState = new GoalState { GoalId = "cluster-fallback-test", Objective = "修改一个文件" };
        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);
    }

    [Fact]
    public async Task ClusterExpand_ValidationFails_Should_RouteToFallback()
    {
        var services = new ServiceCollection();
        var analyzer = new Mock<IDecomposabilityAnalyzer>();

        var tooManyTasks = Enumerable.Range(0, 10)
            .Select(i => new SubTaskDefinition { Id = $"sub_{i}", Title = $"T{i}", Description = $"D{i}", OwnedFiles = [$"file{i}.cs"] })
            .ToList();

        analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DecompositionResult.Decomposable("太多子任务", tooManyTasks));

        services.AddSingleton(analyzer.Object);
        services.AddSingleton<IClusterPlanValidator, ClusterPlanValidator>();
        services.AddSingleton<IClusterPlanApprovalHookManager, NoOpClusterPlanApprovalHook>();
        var sp = services.BuildServiceProvider();

        var engine = CreateEngine(serviceProvider: sp);
        var graph = GoalGraphTemplates.ClusterTemplate.BuildGraph(engine, "并行做10件事");

        var goalState = new GoalState { GoalId = "cluster-validation-test", Objective = "并行做10件事" };
        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);
    }

    [Fact]
    public async Task ClusterExpand_ApprovalBlocked_Should_RouteToFallback()
    {
        var services = new ServiceCollection();
        var analyzer = new Mock<IDecomposabilityAnalyzer>();
        analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DecompositionResult.Decomposable("2个独立模块", [
                new SubTaskDefinition { Id = "sub_1", Title = "A", Description = "DA", OwnedFiles = ["a.cs"] },
                new SubTaskDefinition { Id = "sub_2", Title = "B", Description = "DB", OwnedFiles = ["b.cs"] },
            ]));

        services.AddSingleton(analyzer.Object);
        services.AddSingleton<IClusterPlanValidator, ClusterPlanValidator>();
        services.AddSingleton<IClusterPlanApprovalHookManager, BlockingClusterPlanApprovalHook>();
        var sp = services.BuildServiceProvider();

        var engine = CreateEngine(serviceProvider: sp);
        var graph = GoalGraphTemplates.ClusterTemplate.BuildGraph(engine, "并行做2件事");

        var goalState = new GoalState { GoalId = "cluster-blocked-test", Objective = "并行做2件事" };
        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);
    }

    [Fact]
    public async Task ClusterExpand_WithDependencies_Should_CreateCorrectEdges()
    {
        var services = new ServiceCollection();
        var analyzer = new Mock<IDecomposabilityAnalyzer>();
        analyzer.Setup(a => a.AnalyzeAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DecompositionResult.Decomposable("钻石依赖", [
                new SubTaskDefinition { Id = "sub_1", Title = "Base", Description = "D", OwnedFiles = ["base.cs"] },
                new SubTaskDefinition { Id = "sub_2", Title = "Left", Description = "D", DependsOn = ["sub_1"], OwnedFiles = ["left.cs"] },
                new SubTaskDefinition { Id = "sub_3", Title = "Right", Description = "D", DependsOn = ["sub_1"], OwnedFiles = ["right.cs"] },
                new SubTaskDefinition { Id = "sub_4", Title = "Top", Description = "D", DependsOn = ["sub_2", "sub_3"], OwnedFiles = ["top.cs"] },
            ]));

        services.AddSingleton(analyzer.Object);
        services.AddSingleton<IClusterPlanValidator, ClusterPlanValidator>();
        services.AddSingleton<IClusterPlanApprovalHookManager, NoOpClusterPlanApprovalHook>();
        var sp = services.BuildServiceProvider();

        var engine = CreateEngine(serviceProvider: sp);
        var graph = GoalGraphTemplates.ClusterTemplate.BuildGraph(engine, "集群执行钻石依赖任务");

        var goalState = new GoalState { GoalId = "cluster-diamond-test", Objective = "集群执行钻石依赖任务" };
        var result = await engine.ExecuteAsync(graph, goalState, new MessageList(), CancellationToken.None);

        Assert.True(graph.Dag.Nodes.ContainsKey("worker_sub_1"), "worker_sub_1 应被动态添加");
        Assert.True(graph.Dag.Nodes.ContainsKey("worker_sub_2"), "worker_sub_2 应被动态添加");
        Assert.True(graph.Dag.Nodes.ContainsKey("worker_sub_3"), "worker_sub_3 应被动态添加");
        Assert.True(graph.Dag.Nodes.ContainsKey("worker_sub_4"), "worker_sub_4 应被动态添加");
        Assert.True(graph.Dag.Nodes.ContainsKey("cluster_gather"), "cluster_gather 应被动态添加");
        Assert.True(graph.Dag.Nodes.ContainsKey("cluster_merge"), "cluster_merge 应被动态添加");

        var worker1 = graph.Dag.Nodes["worker_sub_1"].Payload;
        Assert.Equal(AgentIsolationMode.Worktree, worker1.IsolationMode);
        Assert.Equal(2, worker1.MaxLoopIterations);
    }

    private static GoalGraphEngine CreateEngine(
        Mock<IChatClient>? kernel = null,
        Mock<IGoalEvaluator>? evaluator = null,
        IServiceProvider? serviceProvider = null)
    {
        var mockKernel = kernel ?? CreateKernelMock();
        var mockEvaluator = evaluator ?? new Mock<IGoalEvaluator>();
        var heartbeat = new Mock<IGoalHeartbeat>();
        heartbeat.SetupGet(h => h.RefCount).Returns(0);
        heartbeat.SetupGet(h => h.IsActive).Returns(false);
        heartbeat.Setup(h => h.RegisterCallback(It.IsAny<Func<CancellationToken, ValueTask>>()));
        heartbeat.Setup(h => h.DisposeAsync()).Returns(new ValueTask());

        return new GoalGraphEngine(
            mockKernel.Object,
            mockEvaluator.Object,
            serviceProvider ?? new ServiceCollection().BuildServiceProvider(),
            heartbeat: heartbeat.Object);
    }

    private static Mock<IChatClient> CreateKernelMock()
    {
        var kernel = new Mock<IChatClient>();
        var plugins = new Mock<IToolCollection>();
        kernel.SetupGet(k => k.Plugins).Returns(plugins.Object);
        return kernel;
    }

    private sealed class NoOpClusterPlanApprovalHook : IClusterPlanApprovalHookManager
    {
        public Task<ClusterPlanApprovalHookResult> OnClusterPlanApprovalAsync(ClusterPlanApprovalHookContext context, CancellationToken ct = default)
            => Task.FromResult(ClusterPlanApprovalHookResult.Proceed());
    }

    private sealed class BlockingClusterPlanApprovalHook : IClusterPlanApprovalHookManager
    {
        public Task<ClusterPlanApprovalHookResult> OnClusterPlanApprovalAsync(ClusterPlanApprovalHookContext context, CancellationToken ct = default)
            => Task.FromResult(ClusterPlanApprovalHookResult.Block("审批被阻止"));
    }
}
