namespace Core.Goal.Tests;


/// <summary>
/// T2.2: 派发前查热点表 — 热点文件契约改队长自己揽不派Worker
/// </summary>
public sealed partial class GoalGraphEngineTests
{
    private static async IAsyncEnumerable<AgentStreamChunk> CreateFakeAgentStream([EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        ct.ThrowIfCancellationRequested();
        yield return new AgentStreamChunk { Type = AgentStreamChunkType.Content, Content = "fake output", AgentId = "fake-agent" };
        yield return new AgentStreamChunk { Type = AgentStreamChunkType.Complete, Content = "fake output", AgentId = "fake-agent", ExecutionTimeMs = 100 };
    }

    private static DagNode<GoalNodePayload> MakeAgentNode(string id, string name, string[]? ownedFiles = null)
        => new()
        {
            Id = id,
            Payload = new GoalNodePayload
            {
                Kind = GoalNodeKind.Agent,
                Name = name,
                Role = AgentRole.Executor,
                Variant = ExecutorVariant.Code,
                OwnedFiles = ownedFiles,
            },
        };

    [Fact]
    public async Task HotSpotGuard_WhenCaptainShouldHandle_ShouldOverrideRoleToCoordinator()
    {
        AgentSpawnOptions? capturedOptions = null;
        var agentServiceMock = new Mock<IAgentService>();
        agentServiceMock.Setup(a => a.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Callback<AgentSpawnOptions, CancellationToken>((opts, _) => capturedOptions = opts)
            .Returns((AgentSpawnOptions _, CancellationToken ct) => CreateFakeAgentStream(ct));

        var guardMock = new Mock<ICaptainDispatchGuard>();
        guardMock.Setup(g => g.CheckBeforeDispatch(It.IsAny<IReadOnlyList<string>>()))
            .Returns(new DispatchDecision { ShouldCaptainHandle = true, Reason = "热点契约改", HotSpotFiles = ["src/HotFile.cs"] });

        var services = new ServiceCollection();
        services.AddSingleton(agentServiceMock.Object);
        services.AddSingleton(guardMock.Object);
        var engine = CreateEngine(serviceProvider: services.BuildServiceProvider());

        var dag = new Dag<GoalNodePayload>();
        dag.AddNode(MakeAgentNode("agent", "test-agent", ["src/HotFile.cs"]));

        var graph = new GoalGraph { Name = "hotspot-captain-test", Dag = dag, StartNodeId = "agent", EndNodeIds = FrozenSet.Create("agent") };
        await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.NotNull(capturedOptions);
        Assert.Equal(AgentRole.Coordinator, capturedOptions!.Role);
        guardMock.Verify(g => g.CheckBeforeDispatch(It.Is<IReadOnlyList<string>>(l => l.Contains("src/HotFile.cs"))), Times.Once);
    }

    [Fact]
    public async Task HotSpotGuard_WhenWorkerCanHandle_ShouldKeepOriginalRole()
    {
        AgentSpawnOptions? capturedOptions = null;
        var agentServiceMock = new Mock<IAgentService>();
        agentServiceMock.Setup(a => a.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Callback<AgentSpawnOptions, CancellationToken>((opts, _) => capturedOptions = opts)
            .Returns((AgentSpawnOptions _, CancellationToken ct) => CreateFakeAgentStream(ct));

        var guardMock = new Mock<ICaptainDispatchGuard>();
        guardMock.Setup(g => g.CheckBeforeDispatch(It.IsAny<IReadOnlyList<string>>()))
            .Returns(new DispatchDecision { ShouldCaptainHandle = false, Reason = "无热点", HotSpotFiles = [] });

        var services = new ServiceCollection();
        services.AddSingleton(agentServiceMock.Object);
        services.AddSingleton(guardMock.Object);
        var engine = CreateEngine(serviceProvider: services.BuildServiceProvider());

        var dag = new Dag<GoalNodePayload>();
        dag.AddNode(MakeAgentNode("agent", "test-agent", ["src/NormalFile.cs"]));

        var graph = new GoalGraph { Name = "hotspot-worker-test", Dag = dag, StartNodeId = "agent", EndNodeIds = FrozenSet.Create("agent") };
        await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        Assert.NotNull(capturedOptions);
        Assert.Equal(AgentRole.Executor, capturedOptions!.Role);
    }

    [Fact]
    public async Task HotSpotGuard_WhenNoOwnedFiles_ShouldNotCallGuard()
    {
        var agentServiceMock = new Mock<IAgentService>();
        agentServiceMock.Setup(a => a.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Returns((AgentSpawnOptions _, CancellationToken ct) => CreateFakeAgentStream(ct));

        var guardMock = new Mock<ICaptainDispatchGuard>();

        var services = new ServiceCollection();
        services.AddSingleton(agentServiceMock.Object);
        services.AddSingleton(guardMock.Object);
        var engine = CreateEngine(serviceProvider: services.BuildServiceProvider());

        var dag = new Dag<GoalNodePayload>();
        dag.AddNode(MakeAgentNode("agent", "test-agent", ownedFiles: null));

        var graph = new GoalGraph { Name = "no-owned-files-test", Dag = dag, StartNodeId = "agent", EndNodeIds = FrozenSet.Create("agent") };
        await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        guardMock.Verify(g => g.CheckBeforeDispatch(It.IsAny<IReadOnlyList<string>>()), Times.Never);
    }
}
