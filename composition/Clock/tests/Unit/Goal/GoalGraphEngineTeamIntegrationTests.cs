namespace Core.Goal.Tests;


/// <summary>
/// T8.3: /goal 接入 team 组件 — 图执行时建团队，节点派发的 sub-agent 加入团队
/// </summary>
public sealed partial class GoalGraphEngineTests
{
    private static async IAsyncEnumerable<AgentStreamChunk> CreateFakeAgentStreamWithId(string agentId, [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask;
        ct.ThrowIfCancellationRequested();
        yield return new AgentStreamChunk { Type = AgentStreamChunkType.Content, Content = "output", AgentId = agentId };
        yield return new AgentStreamChunk { Type = AgentStreamChunkType.Complete, Content = "output", AgentId = agentId, ExecutionTimeMs = 50 };
    }

    [Fact]
    public async Task TeamIntegration_WhenTeamManagerInjected_ShouldCreateTeamAndAddMember()
    {
        var agentServiceMock = new Mock<IAgentService>();
        agentServiceMock.Setup(a => a.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Returns((AgentSpawnOptions _, CancellationToken ct) => CreateFakeAgentStreamWithId("worker-1", ct));

        var teamManagerMock = new Mock<ITeamManager>();
        teamManagerMock.Setup(t => t.CreateTeamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<TeamInfo?>.Ok(new TeamInfo { TeamId = "team-1", TeamName = "goal-test-goal" }));
        teamManagerMock.Setup(t => t.AddTeamMemberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<TeamInfo?>.Ok(new TeamInfo { TeamId = "team-1", TeamName = "goal-test-goal" }));

        var services = new ServiceCollection();
        services.AddSingleton(agentServiceMock.Object);
        services.AddSingleton(teamManagerMock.Object);
        var engine = CreateEngine(serviceProvider: services.BuildServiceProvider());

        var dag = new Dag<GoalNodePayload>();
        dag.AddNode(MakeAgentNode("agent", "test-agent"));

        var graph = new GoalGraph { Name = "team-test", Dag = dag, StartNodeId = "agent", EndNodeIds = FrozenSet.Create("agent") };
        var state = CreateGoalState();
        await engine.ExecuteAsync(graph, state, new MessageList(), CancellationToken.None);

        teamManagerMock.Verify(t => t.CreateTeamAsync("goal-test-goal", It.IsAny<string>(), null, It.IsAny<CancellationToken>()), Times.Once);
        teamManagerMock.Verify(t => t.AddTeamMemberAsync("team-1", "worker-1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TeamIntegration_WhenNoTeamManager_ShouldSkipTeamCreation()
    {
        var agentServiceMock = new Mock<IAgentService>();
        agentServiceMock.Setup(a => a.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Returns((AgentSpawnOptions _, CancellationToken ct) => CreateFakeAgentStreamWithId("solo-1", ct));

        var services = new ServiceCollection();
        services.AddSingleton(agentServiceMock.Object);
        var engine = CreateEngine(serviceProvider: services.BuildServiceProvider());

        var dag = new Dag<GoalNodePayload>();
        dag.AddNode(MakeAgentNode("agent", "test-agent"));

        var graph = new GoalGraph { Name = "no-team-test", Dag = dag, StartNodeId = "agent", EndNodeIds = FrozenSet.Create("agent") };
        await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        agentServiceMock.Verify(a => a.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TeamIntegration_WhenCreateTeamFails_ShouldDegradeToSoloMode()
    {
        var agentServiceMock = new Mock<IAgentService>();
        agentServiceMock.Setup(a => a.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Returns((AgentSpawnOptions _, CancellationToken ct) => CreateFakeAgentStreamWithId("degraded-1", ct));

        var teamManagerMock = new Mock<ITeamManager>();
        teamManagerMock.Setup(t => t.CreateTeamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(OperationResult<TeamInfo?>.Fail("DB error"));

        var services = new ServiceCollection();
        services.AddSingleton(agentServiceMock.Object);
        services.AddSingleton(teamManagerMock.Object);
        var engine = CreateEngine(serviceProvider: services.BuildServiceProvider());

        var dag = new Dag<GoalNodePayload>();
        dag.AddNode(MakeAgentNode("agent", "test-agent"));

        var graph = new GoalGraph { Name = "degrade-test", Dag = dag, StartNodeId = "agent", EndNodeIds = FrozenSet.Create("agent") };
        await engine.ExecuteAsync(graph, CreateGoalState(), new MessageList(), CancellationToken.None);

        teamManagerMock.Verify(t => t.CreateTeamAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<List<string>>(), It.IsAny<CancellationToken>()), Times.Once);
        teamManagerMock.Verify(t => t.AddTeamMemberAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        agentServiceMock.Verify(a => a.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
