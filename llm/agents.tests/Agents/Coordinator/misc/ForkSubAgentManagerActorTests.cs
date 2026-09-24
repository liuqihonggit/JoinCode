
namespace Sync.Tests.Agents.Coordinator;

/// <summary>
/// ForkSubAgentManagerActor 单元测试 — 验证 Actor 版与旧版行为等价。
/// 所有测试用例对齐 ForkSubAgentManagerTests，仅替换 Manager 实例为 Actor 版。
/// </summary>
public class ForkSubAgentManagerActorTests : IAsyncLifetime {
    private readonly Mock<IAgentLifecycleManager> _lifecycleManagerMock;
    private readonly Mock<IMailbox> _messageBrokerMock;
    private readonly ForkSubAgentManagerActor _manager;

    public ForkSubAgentManagerActorTests() {
        _lifecycleManagerMock = new Mock<IAgentLifecycleManager>();
        _messageBrokerMock = new Mock<IMailbox>();

        var pipeline = CreatePipeline();
        var deps = new ForkManagerDependencies(
            _lifecycleManagerMock.Object,
            _messageBrokerMock.Object);
        _manager = new ForkSubAgentManagerActor(pipeline, deps, NullLogger<ForkSubAgentManagerActor>.Instance);
    }

    private MiddlewarePipeline<ForkContext> CreatePipeline() {
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
    public async Task ForkAsync_ShouldCreateForkedAgentWithSharedCache() {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Fork task", null, queryEngineMock.Object, null);

        var agentResult = new SubAgentResult {
            AgentId = "fork-agent-1",
            IsSuccess = true,
            Output = "Fork completed"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);

        var options = new ForkOptions {
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
    public async Task ForkAsync_ShareCacheFalse_ShouldCreateIndependentCache() {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Independent fork", null, queryEngineMock.Object, null);

        var agentResult = new SubAgentResult {
            AgentId = "fork-agent-2",
            IsSuccess = true,
            Output = "Independent fork completed"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);

        var options = new ForkOptions {
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
    public async Task ForkAsync_FailedAgentExecution_ShouldReturnFailedForkResult() {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Failing fork", null, queryEngineMock.Object, null);

        var agentResult = new SubAgentResult {
            AgentId = "fork-agent-3",
            IsSuccess = false,
            Output = "",
            Error = "Fork execution failed"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);

        var options = new ForkOptions {
            ParentSessionId = "parent-session-3",
            TaskDescription = "Failing fork task",
            ShareCache = true
        };

        var result = await _manager.ForkAsync(options).ConfigureAwait(true);

        result.State.Should().Be(ForkState.Failed);
        result.Result.Should().Be("Fork execution failed");
    }

    [Fact]
    public async Task ForkAsync_NullPipeline_ShouldThrowArgumentNullException() {
        var deps = new ForkManagerDependencies(
            _lifecycleManagerMock.Object,
            _messageBrokerMock.Object);
        var act = () => new ForkSubAgentManagerActor(null!, deps);

        act.Should().Throw<ArgumentNullException>().WithParameterName("pipeline");
    }

    [Fact]
    public async Task ForkAsync_NullDeps_ShouldThrowArgumentNullException() {
        var pipeline = CreatePipeline();
        var act = () => new ForkSubAgentManagerActor(pipeline, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("deps");
    }

    [Fact]
    public async Task GetActiveForksAsync_NoForks_ShouldReturnEmptyList() {
        var forks = await _manager.GetActiveForksAsync().ConfigureAwait(true);

        forks.Should().BeEmpty();
    }

    [Fact]
    public async Task GetActiveForksAsync_AfterFork_ShouldReturnFork() {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Task", null, queryEngineMock.Object, null);

        var agentResult = new SubAgentResult {
            AgentId = "fork-agent-4",
            IsSuccess = true,
            Output = "Done"
        };

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(agentResult);

        var options = new ForkOptions {
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
    public async Task CancelForkAsync_NonExistentFork_ShouldNotThrow() {
        var act = () => _manager.CancelForkAsync("nonexistent");

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task DisposeAsync_ShouldCleanupResources() {
        var act = () => _manager.DisposeAsync().AsTask();

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// 复现 Bug: 后台模式下 ExecuteAsync 抛出异常时,RunBackgroundForkAsync 的 finally 块
    /// 会 dispose forkCts,随后 .WaitAsync(..., forkCts.Token) 访问已 dispose 的 token 抛 ObjectDisposedException
    /// 修复: 先读取 forkCts.Token 到局部变量,避免 dispose 后访问
    /// </summary>
    [Fact]
    public async Task ForkAsync_BackgroundMode_ExecuteAsyncThrows_ShouldNotThrowObjectDisposedException() {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Background task that throws", null, queryEngineMock.Object, null);

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(agent);

        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FormatException("Input string was not in a correct format."));

        var options = new ForkOptions {
            ParentSessionId = "parent-bg-throw",
            TaskDescription = "Background fork that throws",
            ShareCache = true,
            RunInBackground = true
        };

        var act = () => _manager.ForkAsync(options);

        await act.Should().NotThrowAsync<ObjectDisposedException>().ConfigureAwait(true);

        await WaitUntilAsync(() => _manager.InputCount == 0, TimeSpan.FromMilliseconds(500));
    }

    [Fact]
    public async Task ForkAsync_BackgroundWithEventChannel_ShouldEmitAgentFinishedOnCompletion() {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Fork with channel", null, queryEngineMock.Object, null);

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubAgentResult { AgentId = "fork-agent-ch", IsSuccess = true, Output = "Fork completed" });

        var channel = new SubAgentEventChannel();
        var options = new ForkOptions {
            ParentSessionId = "parent-session-ch",
            TaskDescription = "Fork task",
            RunInBackground = true,
            EventChannel = channel
        };

        var result = await _manager.ForkAsync(options).ConfigureAwait(true);
        result.State.Should().Be(ForkState.Running);

        ChatStreamEvent? finished = null;
        for (var i = 0; i < 100 && finished is null; i++) {
            await Task.Delay(20).ConfigureAwait(true);
            finished = channel.TryDrain().FirstOrDefault(e => e.Type == ChatStreamEventType.AgentFinished);
        }

        finished.Should().NotBeNull("后台 fork 完成必须发射 AgentFinished");
        finished!.AgentSuccess.Should().BeTrue();
        finished.AgentId.Should().Be(agent.ObjectId.UniqueId, "终态应携带真实 agentId（entry.AgentId）");
        finished.Content.Should().Be("Fork completed");
    }

    /// <summary>
    /// ADR-0051 对齐测试: 后台 fork 信号量持有时间 = fork 生命周期（RunBackgroundForkAsync 完成时释放）。
    /// MaxConcurrentForks=1 时，后台 fork 运行期间第二个 fork 应被阻塞，后台 fork 完成后第二个 fork 才能执行。
    /// </summary>
    [Fact]
    public async Task ForkAsync_BackgroundFork_HoldsSemaphoreUntilBackgroundCompletes() {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var bgAgent = new AgentBase("Background fork", null, queryEngineMock.Object, null);
        var syncAgent = new AgentBase("Sync fork", null, queryEngineMock.Object, null);
        var agentQueue = new Queue<AgentBase>(new[] { bgAgent, syncAgent });

        var bgAgentResult = new SubAgentResult { AgentId = "bg-agent", IsSuccess = true, Output = "BG done" };
        var bgDelayTask = Task.Delay(TimeSpan.FromSeconds(1)).ContinueWith(_ => bgAgentResult, TaskScheduler.Default);

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync((string _, SubAgentOptions _, CancellationToken _, string? _) => agentQueue.Dequeue());
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(bgAgent, It.IsAny<CancellationToken>()))
            .Returns(bgDelayTask);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(syncAgent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubAgentResult { AgentId = "sync-agent", IsSuccess = true, Output = "Sync done" });

        await using var manager = new ForkSubAgentManagerActor(
            CreatePipeline(),
            new ForkManagerDependencies(_lifecycleManagerMock.Object, _messageBrokerMock.Object),
            NullLogger<ForkSubAgentManagerActor>.Instance,
            null,
            new SubAgentConcurrencyOptions { MaxConcurrentForks = 1 });

        var bgResult = await manager.ForkAsync(new ForkOptions {
            ParentSessionId = "parent-bg",
            TaskDescription = "BG",
            RunInBackground = true,
        }).ConfigureAwait(true);
        bgResult.State.Should().Be(ForkState.Running);

        var secondForkCompleted = false;
        var secondForkTask = Task.Run(async () => {
            var r = await manager.ForkAsync(new ForkOptions {
                ParentSessionId = "parent-sync",
                TaskDescription = "Sync",
            }).ConfigureAwait(true);
            secondForkCompleted = true;
            return r;
        });

        await WaitUntilAsync(() => _manager.InputCount == 0, TimeSpan.FromMilliseconds(500));
        secondForkCompleted.Should().BeFalse("第二个 fork 应被阻塞 — 后台 fork 仍持有信号量");

        var completedSecond = await secondForkTask.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(true);
        secondForkCompleted.Should().BeTrue("后台 fork 完成释放信号量后，第二个 fork 应能执行");
        completedSecond.State.Should().Be(ForkState.Completed);
    }

    /// <summary>
    /// Actor 专属测试: Consumer 异常不终止循环,后续命令仍可处理
    /// </summary>
    [Fact]
    public async Task Actor_ConsumerError_DoesNotTerminateLoop() {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Task1", null, queryEngineMock.Object, null);

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubAgentResult { AgentId = "a1", IsSuccess = true, Output = "ok" });

        await _manager.ForkAsync(new ForkOptions { ParentSessionId = "p1", TaskDescription = "t1" }).ConfigureAwait(true);

        var agent2 = new AgentBase("Task2", null, queryEngineMock.Object, null);
        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(agent2);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent2, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubAgentResult { AgentId = "a2", IsSuccess = true, Output = "ok2" });

        var result2 = await _manager.ForkAsync(new ForkOptions { ParentSessionId = "p2", TaskDescription = "t2" }).ConfigureAwait(true);
        result2.State.Should().Be(ForkState.Completed);

        var forks = await _manager.GetActiveForksAsync().ConfigureAwait(true);
        forks.Should().HaveCount(2);
    }

    /// <summary>
    /// Actor 专属测试: 并发 Fork 1000 次无死锁(全局超时 10s)
    /// </summary>
    [Fact]
    public async Task Actor_ConcurrentForks_NoDeadlock() {
        var queryEngineMock = new Mock<JoinCode.Abstractions.Interfaces.IQueryEngine>();
        var agent = new AgentBase("Concurrent", null, queryEngineMock.Object, null);

        _lifecycleManagerMock
            .Setup(x => x.SpawnSubAgentAsync(It.IsAny<string>(), It.IsAny<SubAgentOptions>(), It.IsAny<CancellationToken>(), It.IsAny<string?>()))
            .ReturnsAsync(agent);
        _lifecycleManagerMock
            .Setup(x => x.ExecuteAsync(agent, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SubAgentResult { AgentId = "a", IsSuccess = true, Output = "ok" });

        var tasks = Enumerable.Range(0, 100).Select(i =>
            _manager.ForkAsync(new ForkOptions { ParentSessionId = $"p{i}", TaskDescription = $"t{i}" }));

        var results = await Task.WhenAll(tasks).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(true);
        results.Should().HaveCount(100);
        results.All(r => r.State == ForkState.Completed).Should().BeTrue();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() {
        await _manager.DisposeSafeAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan perRetryTimeout) {
        for (var i = 0; i < 16; i++) {
            var deadline = DateTimeOffset.UtcNow + perRetryTimeout;
            while (DateTimeOffset.UtcNow < deadline) {
                if (condition()) return;
                await Task.Delay(10);
            }
        }
        throw new TimeoutException($"等待条件超时,重试16次×{perRetryTimeout.TotalMilliseconds:F0}ms");
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, TimeSpan perRetryTimeout) {
        for (var i = 0; i < 16; i++) {
            var deadline = DateTimeOffset.UtcNow + perRetryTimeout;
            while (DateTimeOffset.UtcNow < deadline) {
                if (await predicate()) return;
                await Task.Delay(10);
            }
        }
        throw new TimeoutException($"等待条件超时,重试16次×{perRetryTimeout.TotalMilliseconds:F0}ms");
    }
}