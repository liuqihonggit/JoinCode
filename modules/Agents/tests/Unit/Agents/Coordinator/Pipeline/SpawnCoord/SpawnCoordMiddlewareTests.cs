namespace Core.Tests.Agents.Coordinator.Pipeline.SpawnCoord;

using JoinCode.Abstractions.Clock;

public sealed class SpawnCoordMiddlewareTests
{
    private readonly Mock<IAgentLifecycleManager> _lifecycleManager;
    private readonly Mock<IAgentWorktreeManager> _worktreeManager;
    private readonly Mock<IAgentMessageBroker> _messageBroker;
    private readonly Mock<ISubAgentContextAccessor> _contextAccessor;
    private readonly Mock<IClockService> _clock;
    private readonly Mock<ITeammateLayoutManager> _layoutManager;

    public SpawnCoordMiddlewareTests()
    {
        _lifecycleManager = new Mock<IAgentLifecycleManager>();
        _worktreeManager = new Mock<IAgentWorktreeManager>();
        _messageBroker = new Mock<IAgentMessageBroker>();
        _contextAccessor = new Mock<ISubAgentContextAccessor>();
        _clock = new Mock<IClockService>();
        _layoutManager = new Mock<ITeammateLayoutManager>();
    }

    [Fact]
    public async Task Lifecycle_SpawnsAgent_SetsAgentOnContext()
    {
        var agent = CreateAgent("agent-1");
        _lifecycleManager.Setup(x => x.SpawnSubAgentAsync("test task", null, default)).ReturnsAsync(agent);

        var mw = new SpawnCoordLifecycleMiddleware(_lifecycleManager.Object, NullLogger<SpawnCoordLifecycleMiddleware>.Instance);
        var ctx = new AgentSpawnCoordContext { Task = "test task" };

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Agent.Should().BeSameAs(agent);
    }

    [Fact]
    public async Task Worktree_Disabled_SkipsCreation()
    {
        _worktreeManager.Setup(x => x.IsWorktreeIsolationEnabled).Returns(false);

        var mw = new SpawnCoordWorktreeMiddleware(_worktreeManager.Object, _lifecycleManager.Object, NullLogger<SpawnCoordWorktreeMiddleware>.Instance);
        var ctx = CreateContextWithAgent();

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.WorktreeCreated.Should().BeFalse();
    }

    [Fact]
    public async Task Worktree_CreatedSuccessfully_SetsFlag()
    {
        _worktreeManager.Setup(x => x.IsWorktreeIsolationEnabled).Returns(true);
        _worktreeManager.Setup(x => x.CreateWorktreeAsync("agent-1", default)).ReturnsAsync(true);

        var mw = new SpawnCoordWorktreeMiddleware(_worktreeManager.Object, _lifecycleManager.Object, NullLogger<SpawnCoordWorktreeMiddleware>.Instance);
        var ctx = CreateContextWithAgent();

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.WorktreeCreated.Should().BeTrue();
    }

    [Fact]
    public async Task Worktree_Failed_DisposesAgentAndThrows()
    {
        _worktreeManager.Setup(x => x.IsWorktreeIsolationEnabled).Returns(true);
        _worktreeManager.Setup(x => x.CreateWorktreeAsync("agent-1", default)).ReturnsAsync(false);
        _lifecycleManager.Setup(x => x.DisposeAgentAsync("agent-1", default)).Returns(Task.CompletedTask);

        var mw = new SpawnCoordWorktreeMiddleware(_worktreeManager.Object, _lifecycleManager.Object, NullLogger<SpawnCoordWorktreeMiddleware>.Instance);
        var ctx = CreateContextWithAgent();

        var act = () => mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        _lifecycleManager.Verify(x => x.DisposeAgentAsync("agent-1", default), Times.Once);
    }

    [Fact]
    public async Task RegisterMessage_RegistersAgent_SetsFlag()
    {
        _contextAccessor.Setup(x => x.Current).Returns((SubAgentContext?)null);
        _messageBroker.Setup(x => x.RegisterAgent("agent-1", null));

        var mw = new SpawnCoordRegisterMessageMiddleware(_messageBroker.Object, _contextAccessor.Object, NullLogger<SpawnCoordRegisterMessageMiddleware>.Instance);
        var ctx = CreateContextWithAgent();

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.MessageRegistered.Should().BeTrue();
    }

    [Fact]
    public async Task RegisterMessage_Exception_DoesNotPropagate()
    {
        _contextAccessor.Setup(x => x.Current).Returns((SubAgentContext?)null);
        _messageBroker.Setup(x => x.RegisterAgent("agent-1", null)).Throws(new InvalidOperationException("test"));

        var mw = new SpawnCoordRegisterMessageMiddleware(_messageBroker.Object, _contextAccessor.Object, NullLogger<SpawnCoordRegisterMessageMiddleware>.Instance);
        var ctx = CreateContextWithAgent();

        var act = () => mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RecordContext_SetsSpawnedAtAndExecutionContext()
    {
        var now = DateTime.UtcNow;
        _clock.Setup(x => x.GetUtcNow()).Returns(now);

        var mw = new SpawnCoordRecordContextMiddleware(_clock.Object);
        var ctx = CreateContextWithAgent();

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.SpawnedAt.Should().Be(now);
        ctx.ExecutionContext.Should().NotBeNull();
        ctx.ExecutionContext!.AgentId.Should().Be("agent-1");
        ctx.ExecutionContext.RetryCount.Should().Be(0);
    }

    [Fact]
    public async Task TeammatePane_NoLayoutManager_SkipsCreation()
    {
        var mw = new SpawnCoordTeammatePaneMiddleware(_contextAccessor.Object, NullLogger<SpawnCoordTeammatePaneMiddleware>.Instance);
        var ctx = CreateContextWithAgent();

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.TeammatePaneCreated.Should().BeFalse();
    }

    [Fact]
    public async Task TeammatePane_WithLayoutManager_CreatesPane()
    {
        _contextAccessor.Setup(x => x.Current).Returns((SubAgentContext?)null);
        _layoutManager.Setup(x => x.CreateTeammatePaneAsync("agent-1", "agent", It.IsAny<string>(), default))
            .ReturnsAsync(new CreatePaneResult { PaneId = "pane-1", BackendType = BackendType.InProcess });

        var mw = new SpawnCoordTeammatePaneMiddleware(_contextAccessor.Object, NullLogger<SpawnCoordTeammatePaneMiddleware>.Instance, _layoutManager.Object);
        var ctx = CreateContextWithAgent();

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.TeammatePaneCreated.Should().BeTrue();
    }

    [Fact]
    public async Task TeammatePane_Exception_DoesNotPropagate()
    {
        _contextAccessor.Setup(x => x.Current).Returns((SubAgentContext?)null);
        _layoutManager.Setup(x => x.CreateTeammatePaneAsync("agent-1", "agent", It.IsAny<string>(), default))
            .ThrowsAsync(new InvalidOperationException("test"));

        var mw = new SpawnCoordTeammatePaneMiddleware(_contextAccessor.Object, NullLogger<SpawnCoordTeammatePaneMiddleware>.Instance, _layoutManager.Object);
        var ctx = CreateContextWithAgent();

        var act = () => mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task FullPipeline_AllStepsSucceed_ReturnsAgent()
    {
        var agent = CreateAgent("agent-1");
        _lifecycleManager.Setup(x => x.SpawnSubAgentAsync("test task", null, default)).ReturnsAsync(agent);
        _worktreeManager.Setup(x => x.IsWorktreeIsolationEnabled).Returns(false);
        _contextAccessor.Setup(x => x.Current).Returns((SubAgentContext?)null);
        _messageBroker.Setup(x => x.RegisterAgent("agent-1", null));
        _clock.Setup(x => x.GetUtcNow()).Returns(DateTime.UtcNow);

        var pipeline = new PipelineBuilder<AgentSpawnCoordContext>()
            .Use(new SpawnCoordLifecycleMiddleware(_lifecycleManager.Object, NullLogger<SpawnCoordLifecycleMiddleware>.Instance))
            .Use(new SpawnCoordWorktreeMiddleware(_worktreeManager.Object, _lifecycleManager.Object, NullLogger<SpawnCoordWorktreeMiddleware>.Instance))
            .Use(new SpawnCoordRegisterMessageMiddleware(_messageBroker.Object, _contextAccessor.Object, NullLogger<SpawnCoordRegisterMessageMiddleware>.Instance))
            .Use(new SpawnCoordRecordContextMiddleware(_clock.Object))
            .Use(new SpawnCoordTeammatePaneMiddleware(_contextAccessor.Object, NullLogger<SpawnCoordTeammatePaneMiddleware>.Instance))
            .Build();

        var ctx = new AgentSpawnCoordContext { Task = "test task" };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.Agent.Should().BeSameAs(agent);
        ctx.MessageRegistered.Should().BeTrue();
        ctx.ExecutionContext.Should().NotBeNull();
    }

    [Fact]
    public async Task FullPipeline_WorktreeFailure_ThrowsAndDoesNotRegister()
    {
        var agent = CreateAgent("agent-1");
        _lifecycleManager.Setup(x => x.SpawnSubAgentAsync("test task", null, default)).ReturnsAsync(agent);
        _worktreeManager.Setup(x => x.IsWorktreeIsolationEnabled).Returns(true);
        _worktreeManager.Setup(x => x.CreateWorktreeAsync("agent-1", default)).ReturnsAsync(false);
        _lifecycleManager.Setup(x => x.DisposeAgentAsync("agent-1", default)).Returns(Task.CompletedTask);

        var pipeline = new PipelineBuilder<AgentSpawnCoordContext>()
            .Use(new SpawnCoordLifecycleMiddleware(_lifecycleManager.Object, NullLogger<SpawnCoordLifecycleMiddleware>.Instance))
            .Use(new SpawnCoordWorktreeMiddleware(_worktreeManager.Object, _lifecycleManager.Object, NullLogger<SpawnCoordWorktreeMiddleware>.Instance))
            .Use(new SpawnCoordRegisterMessageMiddleware(_messageBroker.Object, _contextAccessor.Object, NullLogger<SpawnCoordRegisterMessageMiddleware>.Instance))
            .Use(new SpawnCoordRecordContextMiddleware(_clock.Object))
            .Build();

        var ctx = new AgentSpawnCoordContext { Task = "test task" };
        var act = () => pipeline.ExecuteAsync(ctx, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);

        ctx.MessageRegistered.Should().BeFalse();
    }

    private static SubAgent CreateAgent(string id) => new(id, "test task", null, new Mock<IQueryEngine>().Object, null);

    private static AgentSpawnCoordContext CreateContextWithAgent()
    {
        var agent = CreateAgent("agent-1");
        return new AgentSpawnCoordContext { Task = "test task", Agent = agent };
    }
}
