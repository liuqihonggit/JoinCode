
namespace Sync.Tests.Agents.Coordinator;

public class ForkSubAgentManagerTests
{
    private readonly Mock<IAgentLifecycleManager> _lifecycleManagerMock;
    private readonly Mock<IAgentMessageBroker> _messageBrokerMock;
    private readonly ForkSubAgentManager _manager;

    public ForkSubAgentManagerTests()
    {
        _lifecycleManagerMock = new Mock<IAgentLifecycleManager>();
        _messageBrokerMock = new Mock<IAgentMessageBroker>();

        var pipeline = CreatePipeline();
        var deps = new ForkManagerDependencies(
            _lifecycleManagerMock.Object,
            _messageBrokerMock.Object);
        _manager = new ForkSubAgentManager(pipeline, deps, NullLogger<ForkSubAgentManager>.Instance);
    }

    private MiddlewarePipeline<ForkContext> CreatePipeline()
    {
        var middlewares = new IForkMiddleware[]
        {
            new ForkValidationMiddleware(),
            new ForkSpawnMiddleware(_lifecycleManagerMock.Object, _messageBrokerMock.Object),
            new ForkPermissionMiddleware(),
            new ForkExecutionMiddleware(_lifecycleManagerMock.Object)
        };
        return new MiddlewarePipeline<ForkContext>(middlewares);
    }

    [Fact]
    public async Task ForkAsync_ShouldCreateForkedAgentWithSharedCache()
    {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new SubAgent("fork-agent-1", "Fork task", null, queryEngineMock.Object, null);

        var agentResult = new SubAgentResult
        {
            AgentId = "fork-agent-1",
            IsSuccess = true,
            Output = "Fork completed"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);

        var options = new ForkOptions
        {
            ParentSessionId = "parent-session-1",
            TaskDescription = "Fork task",
            ShareCache = true
        };

        var result = await _manager.ForkAsync(options).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.State.Should().Be(ForkState.Completed);
        result.Result.Should().Be("Fork completed");
        result.SharedCache.Should().NotBeNull();
        result.ForkId.Should().StartWith("fork-");
    }

    [Fact]
    public async Task ForkAsync_ShareCacheFalse_ShouldCreateIndependentCache()
    {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new SubAgent("fork-agent-2", "Independent fork", null, queryEngineMock.Object, null);

        var agentResult = new SubAgentResult
        {
            AgentId = "fork-agent-2",
            IsSuccess = true,
            Output = "Independent fork completed"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);

        var options = new ForkOptions
        {
            ParentSessionId = "parent-session-2",
            TaskDescription = "Independent fork task",
            ShareCache = false
        };

        var result = await _manager.ForkAsync(options).ConfigureAwait(true);

        result.State.Should().Be(ForkState.Completed);
        result.SharedCache.Should().NotBeNull();
        result.SharedCache.Should().BeEmpty();
    }

    [Fact]
    public async Task ForkAsync_FailedAgentExecution_ShouldReturnFailedForkResult()
    {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new SubAgent("fork-agent-3", "Failing fork", null, queryEngineMock.Object, null);

        var agentResult = new SubAgentResult
        {
            AgentId = "fork-agent-3",
            IsSuccess = false,
            Output = "",
            Error = "Fork execution failed"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);

        var options = new ForkOptions
        {
            ParentSessionId = "parent-session-3",
            TaskDescription = "Failing fork task",
            ShareCache = true
        };

        var result = await _manager.ForkAsync(options).ConfigureAwait(true);

        result.State.Should().Be(ForkState.Failed);
        result.Result.Should().Be("Fork execution failed");
    }

    [Fact]
    public async Task ForkAsync_NullPipeline_ShouldThrowArgumentNullException()
    {
        var deps = new ForkManagerDependencies(
            _lifecycleManagerMock.Object,
            _messageBrokerMock.Object);
        var act = () => new ForkSubAgentManager(null!, deps);

        act.Should().Throw<ArgumentNullException>().WithParameterName("pipeline");
    }

    [Fact]
    public async Task ForkAsync_NullDeps_ShouldThrowArgumentNullException()
    {
        var pipeline = CreatePipeline();
        var act = () => new ForkSubAgentManager(pipeline, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("deps");
    }

    [Fact]
    public async Task GetActiveForksAsync_NoForks_ShouldReturnEmptyList()
    {
        var forks = await _manager.GetActiveForksAsync().ConfigureAwait(true);

        forks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveForksAsync_AfterFork_ShouldReturnFork()
    {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new SubAgent("fork-agent-4", "Task", null, queryEngineMock.Object, null);

        var agentResult = new SubAgentResult
        {
            AgentId = "fork-agent-4",
            IsSuccess = true,
            Output = "Done"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);

        var options = new ForkOptions
        {
            ParentSessionId = "parent-session-4",
            TaskDescription = "Task",
            ShareCache = true
        };

        await _manager.ForkAsync(options).ConfigureAwait(true);

        var forks = await _manager.GetActiveForksAsync().ConfigureAwait(true);

        forks.Should().NotBeEmpty();
        forks[0].State.Should().Be(ForkState.Completed);
    }

    [Fact]
    public async Task CancelForkAsync_NonExistentFork_ShouldNotThrow()
    {
        var act = () => _manager.CancelForkAsync("nonexistent");

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_ShouldCleanupResources()
    {
        var act = () => _manager.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 复现 Bug: 后台模式下 ExecuteAsync 抛出异常时,RunBackgroundForkAsync 的 finally 块
    /// 会 dispose forkCts,随后 .WaitAsync(..., forkCts.Token) 访问已 dispose 的 token 抛 ObjectDisposedException
    /// 修复: 先读取 forkCts.Token 到局部变量,避免 dispose 后访问
    /// </summary>
    [Fact]
    public async Task ForkAsync_BackgroundMode_ExecuteAsyncThrows_ShouldNotThrowObjectDisposedException()
    {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new SubAgent("fork-bg-throw", "Background task that throws", null, queryEngineMock.Object, null);

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(agent);

        // ExecuteAsync 抛出 FormatException(模拟 JSON 解析错误导致子代理同步失败)
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException("Input string was not in a correct format."));

        var options = new ForkOptions
        {
            ParentSessionId = "parent-bg-throw",
            TaskDescription = "Background fork that throws",
            ShareCache = true,
            RunInBackground = true
        };

        // 修复前: 抛 ObjectDisposedException (forkCts 被 RunBackgroundForkAsync 的 finally dispose 后,
        //         .WaitAsync(TimeSpan.FromSeconds(10), forkCts.Token) 访问已 dispose的 token)
        // 修复后: 正常返回 ForkState.Running(使用局部变量 forkToken 避免dispose后访问)
        var act = () => _manager.ForkAsync(options);

        await act.Should().NotThrowAsync<ObjectDisposedException>().ConfigureAwait(true);

        // 等待 fire-and-forget 后台任务完成,避免影响后续测试
        await Task.Delay(200).ConfigureAwait(true);
    }

    /// <summary>
    /// 复现 Bug: AgentCoordinatorConstants.SystemPrompts.SubAgentSystemMessage 使用了 {Task} 占位符,
    /// 但 string.Format 期望 {0} 这样的数字占位符。
    /// {Task} 中的 'T' 在 offset 21 处被解析时期望 ASCII 数字,抛出 FormatException:
    /// "Input string was not in a correct format. Failure to parse near offset 21. Expected an ASCII digit."
    /// 该异常在 SubAgent.ExecuteAsync 中被 catch,导致 Background Fork 失败。
    /// 修复: 把 {Task} 改成 {0}
    /// </summary>
    [Fact]
    public void SubAgentSystemMessage_FormatString_ShouldBeValidForStringFormat()
    {
        var format = AgentCoordinatorConstants.SystemPrompts.SubAgentSystemMessage;
        var task = "测试任务";

        var act = () => string.Format(format, task);

        act.Should().NotThrow<FormatException>();
        var result = act();
        result.Should().Contain(task);
    }
}
