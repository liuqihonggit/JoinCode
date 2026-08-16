
namespace Sync.Tests.Scheduling.Tasks;

public class InProcessTeammateTaskExecutorTests
{
    private readonly Mock<IAgentLifecycleManager> _lifecycleManagerMock;
    private readonly Mock<IAgentMessageBroker> _messageBrokerMock;
    private readonly InProcessTeammateTaskExecutor _executor;

    public InProcessTeammateTaskExecutorTests()
    {
        _lifecycleManagerMock = new Mock<IAgentLifecycleManager>();
        _messageBrokerMock = new Mock<IAgentMessageBroker>();
        _executor = new InProcessTeammateTaskExecutor(
            _lifecycleManagerMock.Object,
            _messageBrokerMock.Object,
            NullLogger<InProcessTeammateTaskExecutor>.Instance);
    }

    [Fact]
    public async Task ExecuteTeammateAsync_SuccessfulAgentExecution_ShouldReturnSuccessResult()
    {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Test task", null, queryEngineMock.Object, null);

        var agentResult = new SubAgentResult
        {
            AgentId = "agent-1",
            IsSuccess = true,
            Output = "Task completed successfully"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);
        _lifecycleManagerMock
            .Setup(x => x.DisposeAgentAsync(agent.ObjectId.UniqueId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var definition = new InProcessTeammateDefinition
        {
            TaskId = "tm-001",
            TeammateId = "teammate-1",
            Task = "Do something"
        };

        var result = await _executor.ExecuteTeammateAsync(definition).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue();
        result.TaskId.Should().Be("tm-001");
        result.AgentId.Should().Be("teammate-1");
        result.Output.Should().Be("Task completed successfully");
    }

    [Fact]
    public async Task ExecuteTeammateAsync_FailedAgentExecution_ShouldReturnFailureResult()
    {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Failing task", null, queryEngineMock.Object, null);

        var agentResult = new SubAgentResult
        {
            AgentId = "agent-2",
            IsSuccess = false,
            Output = "",
            Error = "Agent failed"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);
        _lifecycleManagerMock
            .Setup(x => x.DisposeAgentAsync(agent.ObjectId.UniqueId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var definition = new InProcessTeammateDefinition
        {
            TaskId = "tm-002",
            TeammateId = "teammate-2",
            Task = "Fail task"
        };

        var result = await _executor.ExecuteTeammateAsync(definition).ConfigureAwait(true);

        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Agent failed");
    }

    [Fact]
    public async Task ExecuteTeammateAsync_NullDefinition_ShouldThrowArgumentNullException()
    {
        var act = () => _executor.ExecuteTeammateAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteTeammateAsync_ShouldRegisterAndUnregisterMessageBroker()
    {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Task", null, queryEngineMock.Object, null);

        var agentResult = new SubAgentResult
        {
            AgentId = "agent-3",
            IsSuccess = true,
            Output = "Done"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);
        _lifecycleManagerMock
            .Setup(x => x.DisposeAgentAsync(agent.ObjectId.UniqueId, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var definition = new InProcessTeammateDefinition
        {
            TaskId = "tm-003",
            TeammateId = "teammate-3",
            Task = "Task"
        };

        await _executor.ExecuteTeammateAsync(definition).ConfigureAwait(true);

        _messageBrokerMock.Verify(x => x.RegisterAgent("teammate-3", It.IsAny<string?>()), Times.Once);
        _messageBrokerMock.Verify(x => x.UnregisterAgent("teammate-3"), Times.Once);
    }

    [Fact]
    public async Task GetActiveTeammatesAsync_NoActiveTeammates_ShouldReturnEmptyList()
    {
        var teammates = await _executor.GetActiveTeammatesAsync().ConfigureAwait(true);

        teammates.Should().BeEmpty();
    }

    [Fact]
    public async Task SendMessageToTeammateAsync_ShouldDelegateToBroker()
    {
        var message = new AgentMsg
        {
            FromAgentId = "coordinator",
            ToAgentId = "teammate-1",
            MessageType = "text",
            Content = "Hello"
        };

        _messageBrokerMock
            .Setup(x => x.SendMessageAsync("teammate-1", message, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _executor.SendMessageToTeammateAsync("teammate-1", message).ConfigureAwait(true);

        result.Should().BeTrue();
        _messageBrokerMock.Verify(x => x.SendMessageAsync("teammate-1", message, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StopTeammateAsync_NonExistentTeammate_ShouldNotThrow()
    {
        var act = () => _executor.StopTeammateAsync("nonexistent");

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteTeammateAsync_ContinuousMode_FailureThenRetry_ShouldContinueLoop()
    {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Continuous task", null, queryEngineMock.Object, null);

        var successResult = new SubAgentResult
        {
            AgentId = "agent-c",
            IsSuccess = true,
            Output = "ok"
        };

        var executeCallCount = 0;
        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(It.IsAny<IAgent>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                var n = Interlocked.Increment(ref executeCallCount);
                if (n == 1)
                {
                    return Task.FromException<SubAgentResult>(new InvalidOperationException("simulated failure"));
                }
                return Task.FromResult(successResult);
            });
        _lifecycleManagerMock
            .Setup(x => x.DisposeAgentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var definition = new InProcessTeammateDefinition
        {
            TaskId = "tm-ct",
            TeammateId = "teammate-ct",
            Task = "Continuous task",
            ContinuousMode = true
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var result = await _executor.ExecuteTeammateAsync(definition, cts.Token).ConfigureAwait(true);

        result.IsSuccess.Should().BeTrue("continuous mode 应立即返回 success");

        var deadline = DateTime.UtcNow.AddSeconds(4);
        while (executeCallCount < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100).ConfigureAwait(true);
        }

        await _executor.StopTeammateAsync("teammate-ct").ConfigureAwait(true);

        executeCallCount.Should().BeGreaterThanOrEqualTo(2, "第一次失败后循环应重试而非退出");
    }
}
