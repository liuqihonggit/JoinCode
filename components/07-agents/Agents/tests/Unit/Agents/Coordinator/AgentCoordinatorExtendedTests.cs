
namespace Core.Tests.Agents.Coordinator;

public class AgentCoordinatorExtendedTests
{
    private readonly Mock<IQueryEngine> _queryEngineMock;
    private readonly Mock<IAgentLifecycleManager> _lifecycleManagerMock;
    private readonly Mock<IAgentWorktreeManager> _worktreeManagerMock;
    private readonly Mock<IAgentMessageBroker> _messageBrokerMock;
    private readonly Mock<IAgentExecutionEngine> _executionEngineMock;
    private readonly AgentCoordinator _coordinator;

    public AgentCoordinatorExtendedTests()
    {
        _queryEngineMock = new Mock<IQueryEngine>();
        _lifecycleManagerMock = new Mock<IAgentLifecycleManager>();
        _worktreeManagerMock = new Mock<IAgentWorktreeManager>();
        _messageBrokerMock = new Mock<IAgentMessageBroker>();
        _executionEngineMock = new Mock<IAgentExecutionEngine>();

        var spawnPipeline = new MiddlewarePipeline<AgentSpawnCoordContext>(
            [new ActionMiddleware<AgentSpawnCoordContext>(async (ctx, next, ct) =>
            {
                ctx.Agent = await _lifecycleManagerMock.Object.SpawnSubAgentAsync(ctx.Task, ctx.Options, ct);
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
    public async Task SpawnSubAgentAsync_ShouldCreateAgentWithUniqueId()
    {
        var agent1 = new SubAgent("agent-1", "Task 1", null, _queryEngineMock.Object, null);
        var agent2 = new SubAgent("agent-2", "Task 2", null, _queryEngineMock.Object, null);

        _lifecycleManagerMock.SetupSequence(x => x.SpawnSubAgentAsync(It.IsAny<string>(), null, default))
            .ReturnsAsync(agent1)
            .ReturnsAsync(agent2);

        var result1 = await _coordinator.SpawnSubAgentAsync("Task 1").ConfigureAwait(true);
        var result2 = await _coordinator.SpawnSubAgentAsync("Task 2").ConfigureAwait(true);

        result1.Id.Should().NotBe(result2.Id);
    }

    [Fact]
    public async Task SpawnSubAgentsAsync_BatchCreation_ShouldCreateAllAgents()
    {
        var tasks = new[] { "Task 1", "Task 2", "Task 3" };

        for (int i = 0; i < tasks.Length; i++)
        {
            var task = tasks[i];
            var agent = new SubAgent($"agent-{i}", task, null, _queryEngineMock.Object, null);
            _lifecycleManagerMock.Setup(x => x.SpawnSubAgentAsync(task, null, default))
                .ReturnsAsync(agent);
        }

        var result = await _coordinator.SpawnSubAgentsAsync(tasks).ConfigureAwait(true);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetStateReport_ShouldReturnCorrectCounts()
    {
        // Arrange
        var report = new AgentStateReport
        {
            TotalAgents = 5,
            PendingCount = 2,
            RunningCount = 2,
            CompletedCount = 1,
            FailedCount = 0,
            CancelledCount = 0
        };

        _lifecycleManagerMock.Setup(x => x.GetStateReportAsync(default)).ReturnsAsync(report);

        // Act
        var result = await _coordinator.GetStateReportAsync().ConfigureAwait(true);

        // Assert
        result.TotalAgents.Should().Be(5);
        result.PendingCount.Should().Be(2);
        result.RunningCount.Should().Be(2);
        result.CompletedCount.Should().Be(1);
    }

    [Fact]
    public async Task CancelAll_ShouldCancelAllAgents()
    {
        // Act
        await _coordinator.CancelAllAsync().ConfigureAwait(true);

        // Assert
        _lifecycleManagerMock.Verify(x => x.CancelAllAsync(default), Times.Once);
    }

    [Fact]
    public async Task GetRunningAgents_ShouldReturnOnlyRunningAgents()
    {
        // Arrange
        var runningAgents = new List<RunningAgentInfo>
        {
            new() { Id = "agent-1", Description = "Task 1", AgentType = "Test" },
            new() { Id = "agent-2", Description = "Task 2", AgentType = "Test" }
        };

        _lifecycleManagerMock.Setup(x => x.GetRunningAgentsAsync(default)).ReturnsAsync(runningAgents);

        // Act
        var result = await _coordinator.GetRunningAgentsAsync().ConfigureAwait(true);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetCoordinatorReport_ShouldMapStateReportCorrectly()
    {
        // Arrange
        var stateReport = new AgentStateReport
        {
            TotalAgents = 3,
            PendingCount = 1,
            RunningCount = 1,
            CompletedCount = 1,
            FailedCount = 0,
            CancelledCount = 0,
            Agents = new List<AgentStateInfo>
            {
                new() { AgentId = "agent-1", Task = "Task 1", CurrentState = TaskExecutionStatus.Pending },
                new() { AgentId = "agent-2", Task = "Task 2", CurrentState = TaskExecutionStatus.Running },
                new() { AgentId = "agent-3", Task = "Task 3", CurrentState = TaskExecutionStatus.Completed }
            }
        };

        _lifecycleManagerMock.Setup(x => x.GetStateReportAsync(default)).ReturnsAsync(stateReport);

        // Act
        var report = await _coordinator.GetStateReportAsync().ConfigureAwait(true);

        // Assert
        report.TotalAgents.Should().Be(3);
        report.PendingCount.Should().Be(1);
        report.RunningCount.Should().Be(1);
        report.CompletedCount.Should().Be(1);
        report.Agents.Should().HaveCount(3);
    }

    [Fact]
    public async Task PauseResumeAgent_ShouldChangeTaskExecutionStatus()
    {
        // Arrange
        var agentId = "test-agent";
        _lifecycleManagerMock.Setup(x => x.PauseAgentAsync(agentId, default)).ReturnsAsync(true);
        _lifecycleManagerMock.Setup(x => x.ResumeAgentAsync(agentId, default)).ReturnsAsync(true);

        // Act & Assert
        (await _coordinator.PauseAgentAsync(agentId).ConfigureAwait(true)).Should().BeTrue();
        (await _coordinator.ResumeAgentAsync(agentId).ConfigureAwait(true)).Should().BeTrue();
    }

    [Fact]
    public async Task CancelAgent_ShouldCancelSpecificAgent()
    {
        // Arrange
        var agentId = "test-agent";
        _lifecycleManagerMock.Setup(x => x.CancelAgentAsync(agentId, default)).ReturnsAsync(true);

        // Act
        var result = await _coordinator.CancelAgentAsync(agentId).ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
        _lifecycleManagerMock.Verify(x => x.CancelAgentAsync(agentId, default), Times.Once);
    }

    [Fact]
    public async Task RetryAsync_ShouldRetryFailedAgent()
    {
        // Arrange - 需要先创建Agent以建立执行上下文
        var agentId = "test-agent";
        var agent = new SubAgent(agentId, "Task", null, _queryEngineMock.Object, null);
        var expectedResult = new SubAgentResult { AgentId = agentId, IsSuccess = true, Output = "Success" };

        _lifecycleManagerMock.Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), null, default)).ReturnsAsync(agent);
        _worktreeManagerMock.Setup(x => x.IsWorktreeIsolationEnabled).Returns(false);
        _lifecycleManagerMock.Setup(x => x.RetryAsync(agentId, default)).ReturnsAsync(expectedResult);

        // 先创建Agent以建立执行上下文
        await _coordinator.SpawnSubAgentAsync("Task").ConfigureAwait(true);

        // Act
        var result = await _coordinator.RetryAsync(agentId).ConfigureAwait(true);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task GetResult_ShouldReturnAgentResult()
    {
        // Arrange
        var agentId = "test-agent";
        var expectedResult = new SubAgentResult { AgentId = agentId, IsSuccess = true, Output = "Output" };

        _lifecycleManagerMock.Setup(x => x.GetResultAsync(agentId, default)).ReturnsAsync(expectedResult);

        // Act
        var result = await _coordinator.GetResultAsync(agentId).ConfigureAwait(true);

        // Assert
        result.Should().Be(expectedResult);
    }

    [Fact]
    public async Task GetAllResults_ShouldReturnAllAgentResults()
    {
        // Arrange
        var results = new Dictionary<string, SubAgentResult>
        {
            ["agent-1"] = new() { AgentId = "agent-1", IsSuccess = true, Output = "Output 1" },
            ["agent-2"] = new() { AgentId = "agent-2", IsSuccess = false, Output = "Output 2" }
        };

        _lifecycleManagerMock.Setup(x => x.GetAllResultsAsync(default)).ReturnsAsync(results);

        // Act
        var result = await _coordinator.GetAllResultsAsync().ConfigureAwait(true);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task DisposeAgentAsync_ShouldCleanupResources()
    {
        // Arrange
        var agentId = "test-agent";
        _worktreeManagerMock.Setup(x => x.IsWorktreeIsolationEnabled).Returns(true);
        _worktreeManagerMock.Setup(x => x.CleanupWorktreeAsync(agentId, default))
            .ReturnsAsync(WorktreeCleanupDetail.SuccessfullyRemoved);

        // Act
        await _coordinator.DisposeAgentAsync(agentId).ConfigureAwait(true);

        // Assert
        _messageBrokerMock.Verify(x => x.UnregisterAgent(agentId), Times.Once);
        _worktreeManagerMock.Verify(x => x.CleanupWorktreeAsync(agentId, default), Times.Once);
        _lifecycleManagerMock.Verify(x => x.DisposeAgentAsync(agentId, default), Times.Once);
    }

    [Fact]
    public async Task DisposeAgentAsync_WhenWorktreeDisabled_ShouldNotCleanupWorktree()
    {
        // Arrange
        var agentId = "test-agent";
        _worktreeManagerMock.Setup(x => x.IsWorktreeIsolationEnabled).Returns(false);

        // Act
        await _coordinator.DisposeAgentAsync(agentId).ConfigureAwait(true);

        // Assert
        _messageBrokerMock.Verify(x => x.UnregisterAgent(agentId), Times.Once);
        _worktreeManagerMock.Verify(x => x.CleanupWorktreeAsync(agentId), Times.Never);
        _lifecycleManagerMock.Verify(x => x.DisposeAgentAsync(agentId, default), Times.Once);
    }

    [Fact]
    public async Task ExecuteParallelAsync_ShouldExecuteAgentsInParallel()
    {
        // Arrange
        var agents = new List<SubAgent>();
        var expectedResults = new List<SubAgentResult>();

        _executionEngineMock.Setup(x => x.ExecuteParallelAsync(agents, null, default))
            .ReturnsAsync(expectedResults);

        // Act
        var results = await _coordinator.ExecuteParallelAsync(agents).ConfigureAwait(true);

        // Assert
        results.Should().BeEquivalentTo(expectedResults);
    }

    [Fact]
    public async Task ExecuteSequentialAsync_ShouldExecuteAgentsSequentially()
    {
        // Arrange
        var agents = new List<SubAgent>();
        var expectedResults = new List<SubAgentResult>();

        _executionEngineMock.Setup(x => x.ExecuteSequentialAsync(agents, default))
            .ReturnsAsync(expectedResults);

        // Act
        var results = await _coordinator.ExecuteSequentialAsync(agents).ConfigureAwait(true);

        // Assert
        results.Should().BeEquivalentTo(expectedResults);
    }

    [Fact]
    public async Task SendMessageAsync_ShouldSendMessageToAgent()
    {
        // Arrange
        var agentId = "test-agent";
        var message = new AgentMsg { FromAgentId = "sender", ToAgentId = agentId, MessageType = "text", Content = "Hello" };
        var agent = new SubAgent(agentId, "Task", null, _queryEngineMock.Object, null);
        agent.State = TaskExecutionStatus.Running;

        _lifecycleManagerMock.Setup(x => x.GetAgentAsync(agentId, default)).ReturnsAsync(agent);
        _messageBrokerMock.Setup(x => x.SendMessageAsync(agentId, message, default)).ReturnsAsync(true);

        // Act
        var result = await _coordinator.SendMessageAsync(agentId, message).ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task BroadcastAsync_ShouldBroadcastMessageToAllAgents()
    {
        // Arrange
        var message = new AgentMsg { FromAgentId = "sender", ToAgentId = "broadcast", MessageType = "text", Content = "Hello everyone" };

        // Act
        await _coordinator.BroadcastAsync(message).ConfigureAwait(true);

        // Assert
        _messageBrokerMock.Verify(x => x.BroadcastAsync(message, default), Times.Once);
    }

    private static AgentWorktreeSession CreateTestWorktreeSession(string agentId, string worktreePath)
    {
        return new AgentWorktreeSession
        {
            AgentId = agentId,
            WorktreePath = worktreePath,
            OriginalCwd = "/original",
            BranchName = $"agent-{agentId}",
            GitRootPath = "/git/root",
            CreatedAt = DateTime.UtcNow
        };
    }

    [Fact]
    public async Task GetWorktreeSession_ShouldReturnWorktreeSession()
    {
        // Arrange
        var agentId = "test-agent";
        var expectedSession = CreateTestWorktreeSession(agentId, "/path/to/worktree");

        _worktreeManagerMock.Setup(x => x.GetWorktreeSessionAsync(agentId, default)).ReturnsAsync(expectedSession);

        // Act
        var session = await _coordinator.GetWorktreeSessionAsync(agentId).ConfigureAwait(true);

        // Assert
        session.Should().Be(expectedSession);
    }

    [Fact]
    public async Task GetAllWorktreeSessions_ShouldReturnAllSessions()
    {
        // Arrange
        var sessions = new Dictionary<string, AgentWorktreeSession>
        {
            ["agent-1"] = CreateTestWorktreeSession("agent-1", "/path/1"),
            ["agent-2"] = CreateTestWorktreeSession("agent-2", "/path/2")
        };

        _worktreeManagerMock.Setup(x => x.GetAllWorktreeSessionsAsync(default)).ReturnsAsync(sessions);

        // Act
        var result = await _coordinator.GetAllWorktreeSessionsAsync().ConfigureAwait(true);

        // Assert
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task StopAgentAsync_ShouldStopRunningAgent()
    {
        // Arrange
        var agentId = "test-agent";
        var agent = new SubAgent(agentId, "Task", null, _queryEngineMock.Object, null);
        agent.State = TaskExecutionStatus.Running;

        _lifecycleManagerMock.Setup(x => x.GetAgentAsync(agentId, default)).ReturnsAsync(agent);
        _lifecycleManagerMock.Setup(x => x.CancelAgentAsync(agentId, default)).ReturnsAsync(true);

        // Act
        var result = await _coordinator.StopAgentAsync(agentId).ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
    }
}

file sealed class ActionMiddleware<TContext>(Func<TContext, MiddlewareDelegate<TContext>, CancellationToken, Task> invoke) : IMiddleware<TContext>
{
    public Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct) => invoke(context, next, ct);
}
