
namespace Core.Tests.Agents.Coordinator;

public class AgentCoordinatorSecretaryTests
{
    private readonly Mock<IQueryEngine> _queryEngineMock;
    private readonly Mock<IAgentLifecycleManager> _lifecycleManagerMock;
    private readonly Mock<IAgentWorktreeManager> _worktreeManagerMock;
    private readonly Mock<IMailbox> _messageBrokerMock;
    private readonly Mock<IAgentExecutionEngine> _executionEngineMock;
    private readonly AgentCoordinator _coordinator;

    public AgentCoordinatorSecretaryTests()
    {
        _queryEngineMock = new Mock<IQueryEngine>();
        _lifecycleManagerMock = new Mock<IAgentLifecycleManager>();
        _worktreeManagerMock = new Mock<IAgentWorktreeManager>();
        _messageBrokerMock = new Mock<IMailbox>();
        _executionEngineMock = new Mock<IAgentExecutionEngine>();

        var spawnPipeline = new MiddlewarePipeline<UnifiedSpawnContext>(
            [new ActionMiddleware<UnifiedSpawnContext>(async (ctx, next, ct) =>
            {
                ctx.Agent = await _lifecycleManagerMock.Object.SpawnSubAgentAsync(ctx.Task, ctx.SubOptions, ct);
                ctx.ExecutionContext = new AgentExecutionContext
                {
                    AgentId = ctx.AgentId,
                    Task = ctx.Task,
                    SpawnedAt = JoinCode.Abstractions.Clock.SystemClockService.Instance.GetUtcNow(),
                    RetryCount = 0
                };
                await next(ctx, ct);
            })], onError: (_, _) => { });

        var disposePipeline = new MiddlewarePipeline<AgentDisposeContext>(
            [new ActionMiddleware<AgentDisposeContext>(async (ctx, next, ct) =>
            {
                await _lifecycleManagerMock.Object.DisposeAgentAsync(ctx.AgentId, ct);
                _messageBrokerMock.Object.UnregisterAgent(ctx.AgentId);
                if (_worktreeManagerMock.Object.IsWorktreeIsolationEnabled)
                {
                    await _worktreeManagerMock.Object.CleanupWorktreeAsync(ctx.AgentId, ct);
                }
                await next(ctx, ct);
            })], onError: (_, _) => { });

        _coordinator = new AgentCoordinator(
            new AgentCoreDependencies(
                _lifecycleManagerMock.Object,
                _worktreeManagerMock.Object,
                _messageBrokerMock.Object,
                _executionEngineMock.Object,
                new AgentStateMachine()),
            JoinCode.Abstractions.Clock.SystemClockService.Instance,
            disposePipeline,
            spawnPipeline,
            logger: NullLogger<AgentCoordinator>.Instance);
    }

    [Fact]
    public async Task EnsureSecretary_ShouldSpawnTeammateVariantAgent()
    {
        var fakeAgent = new AgentBase("等待队长指令", null, _queryEngineMock.Object, null);
        _lifecycleManagerMock.Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(fakeAgent);

        var secretaryId = await _coordinator.EnsureSecretaryAsync("captain-1");

        secretaryId.Should().NotBeNullOrEmpty();
        _lifecycleManagerMock.Verify(x => x.SpawnSubAgentAsync(
            It.IsAny<string>(),
            It.Is<SubAgentOptions>(o => o.Variant == ExecutorVariant.Teammate && o.DisplayName == "秘书"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EnsureSecretary_CalledTwice_ShouldNotRespawn()
    {
        var fakeAgent = new AgentBase("等待队长指令", null, _queryEngineMock.Object, null);
        _lifecycleManagerMock.Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(fakeAgent);

        var id1 = await _coordinator.EnsureSecretaryAsync("captain-1");
        var id2 = await _coordinator.EnsureSecretaryAsync("captain-1");

        id1.Should().Be(id2);
        _lifecycleManagerMock.Verify(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Once);
    }

    [Fact]
    public async Task EnsureSecretary_DifferentOwners_ShouldSpawnSeparateSecretaries()
    {
        var agent1 = new AgentBase("等待队长指令", null, _queryEngineMock.Object, null);
        var agent2 = new AgentBase("等待队长指令", null, _queryEngineMock.Object, null);
        _lifecycleManagerMock.SetupSequence(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(agent1)
            .ReturnsAsync(agent2);

        var id1 = await _coordinator.EnsureSecretaryAsync("captain-A");
        var id2 = await _coordinator.EnsureSecretaryAsync("captain-B");

        id1.Should().NotBe(id2);
        _lifecycleManagerMock.Verify(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task EnsureSecretary_EmptyOwnerId_ShouldThrow()
    {
        var act = () => _coordinator.EnsureSecretaryAsync("");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public void GetSecretaryId_WhenNotSpawned_ShouldReturnNull()
    {
        _coordinator.GetSecretaryId("captain-1").Should().BeNull();
    }

    [Fact]
    public async Task GetSecretaryId_WhenSpawned_ShouldReturnId()
    {
        var fakeAgent = new AgentBase("等待队长指令", null, _queryEngineMock.Object, null);
        _lifecycleManagerMock.Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(fakeAgent);

        var spawnedId = await _coordinator.EnsureSecretaryAsync("captain-1");
        _coordinator.GetSecretaryId("captain-1").Should().Be(spawnedId);
    }
}

file sealed class ActionMiddleware<TContext>(Func<TContext, MiddlewareDelegate<TContext>, CancellationToken, Task> invoke) : IMiddleware<TContext>
{
    public Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct) => invoke(context, next, ct);
}
