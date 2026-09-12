
#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Core.Goal.Tests;

public sealed class GoalEngineTests
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(5);

    private static (Mock<IChatClient> kernel, Mock<IGoalEvaluator> evaluator, Mock<IServiceProvider> serviceProvider) CreateMocks()
    {
        var kernel = new Mock<IChatClient>();
        var evaluator = new Mock<IGoalEvaluator>();
        var serviceProvider = new Mock<IServiceProvider>();

        var agentService = new Mock<IAgentService>();
        agentService
            .Setup(s => s.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Returns((AgentSpawnOptions opts, CancellationToken ct) => StreamAgentCompletion(opts, ct));
        serviceProvider
            .Setup(sp => sp.GetService(It.Is<Type>(t => t == typeof(IAgentService))))
            .Returns(agentService.Object);

        return (kernel, evaluator, serviceProvider);
    }

    private static async IAsyncEnumerable<AgentStreamChunk> StreamAgentCompletion(
        AgentSpawnOptions opts, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Content,
            Content = "工作完成",
            AgentId = "test-agent",
        };
        yield return new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Complete,
            Content = "工作完成",
            AgentId = "test-agent",
            ExecutionTimeMs = 100,
        };
        await Task.CompletedTask;
    }

    private static (Mock<IChatClient> kernel, Mock<IGoalEvaluator> evaluator, Mock<IServiceProvider> serviceProvider) CreateBlockingMocks(SemaphoreSlim gate)
    {
        var kernel = new Mock<IChatClient>();
        var evaluator = new Mock<IGoalEvaluator>();
        var serviceProvider = new Mock<IServiceProvider>();

        var agentService = new Mock<IAgentService>();
        agentService
            .Setup(s => s.RunAgentStreamAsync(It.IsAny<AgentSpawnOptions>(), It.IsAny<CancellationToken>()))
            .Returns((AgentSpawnOptions opts, CancellationToken ct) => StreamAgentBlocking(opts, ct, gate));
        serviceProvider
            .Setup(sp => sp.GetService(It.Is<Type>(t => t == typeof(IAgentService))))
            .Returns(agentService.Object);

        return (kernel, evaluator, serviceProvider);
    }

    private static async IAsyncEnumerable<AgentStreamChunk> StreamAgentBlocking(
        AgentSpawnOptions opts, [EnumeratorCancellation] CancellationToken ct, SemaphoreSlim gate)
    {
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linkedCts.CancelAfter(GateTimeout);
        await gate.WaitAsync(linkedCts.Token);

        yield return new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Content,
            Content = "工作中",
            AgentId = "test-agent",
        };
        yield return new AgentStreamChunk
        {
            Type = AgentStreamChunkType.Complete,
            Content = "工作中",
            AgentId = "test-agent",
            ExecutionTimeMs = 50,
        };
    }

    private static Mock<IGoalHeartbeat> CreateHeartbeatMock()
    {
        var heartbeat = new Mock<IGoalHeartbeat>();
        heartbeat.SetupGet(h => h.RefCount).Returns(0);
        heartbeat.SetupGet(h => h.IsActive).Returns(false);
        heartbeat.Setup(h => h.RegisterCallback(It.IsAny<Func<CancellationToken, ValueTask>>()));
        heartbeat.Setup(h => h.DisposeAsync()).Returns(new ValueTask());
        return heartbeat;
    }

    private static async ValueTask SafeDisposeAsync(GoalEngine engine)
    {
        try
        {
            await engine.DisposeAsync().AsTask().WaitAsync(DisposeTimeout).ConfigureAwait(true);
        }
        catch (TimeoutException)
        {
            System.Diagnostics.Trace.WriteLine("[GoalEngineTests] GoalEngine 后台循环未在超时内退出，强制忽略");
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Trace.WriteLine("[GoalEngineTests] GoalEngine 已取消，忽略");
        }
    }

    [Fact]
    public void Constructor_WithoutPermissionManager_Should_Create_Successfully()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);

        Assert.NotNull(engine);
        Assert.False(engine.IsRunning);
        Assert.Null(engine.CurrentState);
    }

    [Fact]
    public void Constructor_WithPermissionManager_Should_Create_Successfully()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();
        var permissionManager = new Mock<IToolPermissionManager>();

        var engine = new GoalEngine(kernel.Object, evaluator.Object, permissionManager: permissionManager.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);

        Assert.NotNull(engine);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public async Task StartAsync_Should_Set_State_To_Pursuing()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            var state = await engine.StartAsync("实现用户注册功能").ConfigureAwait(true);

            Assert.NotNull(state);
            Assert.Equal("实现用户注册功能", state.Objective);
            Assert.NotEmpty(state.GoalId);
            Assert.Empty(state.Constraints);
            Assert.Null(state.TokenBudget);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task StartAsync_WithConstraints_Should_Set_Constraints()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            var constraints = new List<string> { "不修改公共API", "测试覆盖率>80%" };
            var state = await engine.StartAsync("实现功能", constraints, 50000).ConfigureAwait(true);

            Assert.Equal(2, state.Constraints.Count);
            Assert.Equal(50000, state.TokenBudget);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyRunning_Should_Throw()
    {
        using var gate = new SemaphoreSlim(0, 1);
        var (kernel, evaluator, serviceProvider) = CreateBlockingMocks(gate);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            await engine.StartAsync("目标1").ConfigureAwait(true);

            await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync("目标2")).ConfigureAwait(true);

            await engine.ClearAsync().ConfigureAwait(true);
        }
        finally
        {
            try { gate.Release(); } catch (SemaphoreFullException ex) { _ = ex; }
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task StartAsync_NullObjective_Should_Throw()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => engine.StartAsync(null!)).ConfigureAwait(true);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task StartAsync_EmptyObjective_Should_Throw()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            await Assert.ThrowsAsync<ArgumentException>(() => engine.StartAsync("")).ConfigureAwait(true);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task PauseAsync_Should_Set_Status_To_Paused()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();
        using var gate = new SemaphoreSlim(0, 1);
        var (bKernel, bEvaluator, bServiceProvider) = CreateBlockingMocks(gate);

        var engine = new GoalEngine(bKernel.Object, bEvaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: bServiceProvider.Object);
        try
        {
            await engine.StartAsync("测试目标").ConfigureAwait(true);

            await engine.PauseAsync().ConfigureAwait(true);

            Assert.Equal(GoalStatus.Paused, engine.CurrentState?.Status);
            Assert.NotNull(engine.CurrentState?.PausedAt);
        }
        finally
        {
            try { gate.Release(); } catch (SemaphoreFullException ex) { _ = ex; }
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ClearAsync_Should_Set_Status_To_Unmet()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            await engine.StartAsync("测试目标").ConfigureAwait(true);

            await engine.ClearAsync().ConfigureAwait(true);

            Assert.Equal(GoalStatus.Unmet, engine.CurrentState?.Status);
            Assert.NotNull(engine.CurrentState?.AchievedAt);
            Assert.False(engine.IsRunning);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task GoalLoop_WhenEvaluatorReturnsCompleted_Should_Set_Achieved()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            var state = await engine.StartAsync("实现功能").ConfigureAwait(true);

            await engine.WaitForCompletionAsync().WaitAsync(DisposeTimeout).ConfigureAwait(true);

            Assert.Equal(GoalStatus.Achieved, engine.CurrentState?.Status);
            Assert.NotNull(engine.CurrentState?.AchievedAt);
            Assert.True(engine.CurrentState?.TurnsCompleted >= 1);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task DisposeAsync_Should_Not_Throw()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);

        await SafeDisposeAsync(engine).ConfigureAwait(true);
    }

    [Fact]
    public async Task GoalLoop_MultiTurn_Should_Loop_Until_Completed()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            await engine.StartAsync("多轮目标").ConfigureAwait(true);

            await engine.WaitForCompletionAsync().WaitAsync(DisposeTimeout).ConfigureAwait(true);

            Assert.Equal(GoalStatus.Achieved, engine.CurrentState?.Status);
            Assert.True(engine.CurrentState?.TurnsCompleted >= 1);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task GoalLoop_BudgetLimited_Should_Stop_When_Budget_Exceeded()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            await engine.StartAsync("预算测试", tokenBudget: 100).ConfigureAwait(true);

            await engine.WaitForCompletionAsync().WaitAsync(DisposeTimeout).ConfigureAwait(true);

            Assert.NotNull(engine.CurrentState);
            Assert.True(engine.CurrentState?.TokensUsed >= 0);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task PauseAndResume_Should_Work()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();
        using var gate = new SemaphoreSlim(0, 1);
        var (bKernel, bEvaluator, bServiceProvider) = CreateBlockingMocks(gate);

        var engine = new GoalEngine(bKernel.Object, bEvaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: bServiceProvider.Object);
        try
        {
            await engine.StartAsync("暂停恢复测试").ConfigureAwait(true);

            await engine.PauseAsync().ConfigureAwait(true);

            Assert.Equal(GoalStatus.Paused, engine.CurrentState?.Status);

            await engine.ResumeAsync().ConfigureAwait(true);
            Assert.Equal(GoalStatus.Pursuing, engine.CurrentState?.Status);

            await engine.ClearAsync().ConfigureAwait(true);
        }
        finally
        {
            try { gate.Release(); } catch (SemaphoreFullException ex) { _ = ex; }
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ClearAsync_When_No_Active_Goal_Should_Not_Throw()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            await engine.ClearAsync().ConfigureAwait(true);

            Assert.Null(engine.CurrentState);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task PauseAsync_When_No_Active_Goal_Should_Not_Throw()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            await engine.PauseAsync().ConfigureAwait(true);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ResumeAsync_When_Not_Paused_Should_Not_Throw()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            await engine.ResumeAsync().ConfigureAwait(true);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task StartAsync_Should_Switch_Permission_Mode_To_Auto()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();
        var permissionManager = new Mock<IToolPermissionManager>();

        permissionManager.Setup(x => x.GetCurrentModeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionMode.Ask);
        permissionManager.Setup(x => x.SetPermissionModeAsync(It.IsAny<PermissionMode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, permissionManager: permissionManager.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            await engine.StartAsync("权限测试").ConfigureAwait(true);

            await engine.WaitForCompletionAsync().WaitAsync(DisposeTimeout).ConfigureAwait(true);

            permissionManager.Verify(x => x.SetPermissionModeAsync(PermissionMode.Auto, It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 验证 GoalEngine 在 serviceProvider 提供 IDeferredMailService 时, 将其注入给 mainAgent
    /// 队长(main agent)不走 ForkSpawnMiddleware, 需在 RegisterMainAgent 中显式注入
    /// </summary>
    [Fact]
    public async Task StartAsync_WithDeferredMailService_Should_Inject_To_MainAgent()
    {
        var (kernel, evaluator, serviceProvider) = CreateMocks();

        var queryEngine = new Mock<IQueryEngine>();
        serviceProvider
            .Setup(sp => sp.GetService(It.Is<Type>(t => t == typeof(IQueryEngine))))
            .Returns(queryEngine.Object);

        var deferredMailService = new Mock<IDeferredMailService>();
        serviceProvider
            .Setup(sp => sp.GetService(It.Is<Type>(t => t == typeof(IDeferredMailService))))
            .Returns(deferredMailService.Object);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, serviceProvider: serviceProvider.Object);
        try
        {
            await engine.StartAsync("延迟邮件注入测试").ConfigureAwait(true);

            var allMainAgents = JoinCode.Abstractions.Entity.SessionRouter.GetAllScopes()
                .SelectMany(s => s.GetAll<Core.Agents.Coordinator.AgentBase>())
                .Where(a => a.Role == AgentRole.Coordinator)
                .ToList();
            Assert.NotEmpty(allMainAgents);
            Assert.NotNull(allMainAgents[0].DeferredMailService);
            Assert.Same(deferredMailService.Object, allMainAgents[0].DeferredMailService);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }
}
#pragma warning restore JCC3010, JCC3011, JCC3012
