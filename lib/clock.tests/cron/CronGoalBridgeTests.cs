
namespace Core.Goal.Tests;

public sealed class CronGoalBridgeTests
{
    private static (Mock<ICronTaskStore> taskStore, Mock<IGoalEngine> goalEngine, CronGoalBridge bridge) CreateBridge(
        IAgentDefinitionProvider? agentProvider = null,
        ILogger<CronGoalBridge>? logger = null)
    {
        var taskStore = new Mock<ICronTaskStore>();
        var goalEngine = new Mock<IGoalEngine>();
        var bridge = new CronGoalBridge(taskStore.Object, goalEngine.Object, agentProvider, logger);
        return (taskStore, goalEngine, bridge);
    }

    [Fact]
    public void Constructor_NullTaskStore_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CronGoalBridge(null!, Mock.Of<IGoalEngine>()));
    }

    [Fact]
    public void Constructor_NullGoalEngine_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CronGoalBridge(Mock.Of<ICronTaskStore>(), null!));
    }

    [Fact]
    public async Task StartAsync_WhenNotStarted_StartsScheduler()
    {
        var (_, _, bridge) = CreateBridge();

        Assert.False(bridge.IsStarted);

        await bridge.StartAsync().ConfigureAwait(true);

        Assert.True(bridge.IsStarted);

        await bridge.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StartAsync_WhenAlreadyStarted_DoesNothing()
    {
        var (_, _, bridge) = CreateBridge();

        await bridge.StartAsync().ConfigureAwait(true);
        await bridge.StartAsync().ConfigureAwait(true);

        Assert.True(bridge.IsStarted);

        await bridge.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task StopAsync_WhenStarted_StopsScheduler()
    {
        var (_, _, bridge) = CreateBridge();

        await bridge.StartAsync().ConfigureAwait(true);
        await bridge.StopAsync().ConfigureAwait(true);

        Assert.False(bridge.IsStarted);
    }

    [Fact]
    public async Task StopAsync_WhenNotStarted_DoesNothing()
    {
        var (_, _, bridge) = CreateBridge();

        await bridge.StopAsync().ConfigureAwait(true);

        Assert.False(bridge.IsStarted);
    }

    [Fact]
    public async Task DisposeAsync_StopsAndDisposes()
    {
        var (_, _, bridge) = CreateBridge();

        await bridge.StartAsync().ConfigureAwait(true);
        await bridge.DisposeAsync().ConfigureAwait(true);

        Assert.False(bridge.IsStarted);
    }

    [Fact]
    public async Task HandleCronFireAsync_WhenGoalEngineRunning_Skips()
    {
        var (_, goalEngine, bridge) = CreateBridge();
        goalEngine.Setup(g => g.IsRunning).Returns(true);

        await bridge.HandleCronFireAsync(new CronTask
        {
            Id = "t1",
            CronExpression = "0 9 * * *",
            Prompt = "test",
            CreatedAt = 0
        }).ConfigureAwait(true);

        goalEngine.Verify(g => g.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);

        await bridge.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task HandleCronFireAsync_WhenNotRunning_StartsGoal()
    {
        var (_, goalEngine, bridge) = CreateBridge();
        goalEngine.Setup(g => g.IsRunning).Returns(false);
        goalEngine.Setup(g => g.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GoalState());

        await bridge.HandleCronFireAsync(new CronTask
        {
            Id = "t1",
            CronExpression = "0 9 * * *",
            Prompt = "run task",
            CreatedAt = 0
        }).ConfigureAwait(true);

        goalEngine.Verify(g => g.StartAsync("run task", null, null, null, It.IsAny<CancellationToken>()), Times.Once);

        await bridge.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task HandleCronFireAsync_WhenStartThrowsInvalidOperation_LogsAndContinues()
    {
        var logger = new Mock<ILogger<CronGoalBridge>>();
        var (_, goalEngine, bridge) = CreateBridge(logger: logger.Object);
        goalEngine.Setup(g => g.IsRunning).Returns(false);
        goalEngine.Setup(g => g.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("已有目标正在运行"));

        await bridge.HandleCronFireAsync(new CronTask
        {
            Id = "t1",
            CronExpression = "0 9 * * *",
            Prompt = "run task",
            CreatedAt = 0
        }).ConfigureAwait(true);

        goalEngine.Verify(g => g.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);

        await bridge.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task HandleCronFireAsync_WhenStartThrowsException_LogsAndContinues()
    {
        var logger = new Mock<ILogger<CronGoalBridge>>();
        var (_, goalEngine, bridge) = CreateBridge(logger: logger.Object);
        goalEngine.Setup(g => g.IsRunning).Returns(false);
        goalEngine.Setup(g => g.StartAsync(It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("boom"));

        await bridge.HandleCronFireAsync(new CronTask
        {
            Id = "t1",
            CronExpression = "0 9 * * *",
            Prompt = "run task",
            CreatedAt = 0
        }).ConfigureAwait(true);

        await bridge.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RegisterBackgroundAgentCronTasksAsync_WhenNoProvider_DoesNothing()
    {
        var (taskStore, _, bridge) = CreateBridge();

        await bridge.StartAsync().ConfigureAwait(true);

        taskStore.Verify(t => t.AddTaskAsync(It.IsAny<CreateCronTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        await bridge.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RegisterBackgroundAgentCronTasksAsync_WithBackgroundAgents_RegistersTasks()
    {
        var taskStore = new Mock<ICronTaskStore>();
        var agentProvider = new Mock<IAgentDefinitionProvider>();
        agentProvider.Setup(a => a.GetAgentDefinitionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentDefinition>
            {
                new() { Role = AgentRole.Executor, Variant = ExecutorVariant.Doctor, IsBackground = true, WhenToUse = "维护关键词" }
            });
        taskStore.Setup(t => t.GetAllTasksAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<CronTask>());
        taskStore.Setup(t => t.AddTaskAsync(It.IsAny<CreateCronTaskRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CreateCronTaskRequest r, CancellationToken _) => new CronTask
            {
                Id = "id1",
                CronExpression = r.CronExpression,
                Prompt = r.Prompt,
                CreatedAt = 0,
                IsRecurring = r.IsRecurring,
                IsDurable = r.IsDurable
            });

        var goalEngine = new Mock<IGoalEngine>();
        var bridge = new CronGoalBridge(taskStore.Object, goalEngine.Object, agentProvider.Object);

        await bridge.StartAsync().ConfigureAwait(true);

        taskStore.Verify(t => t.AddTaskAsync(It.Is<CreateCronTaskRequest>(r => r.CronExpression == "0 */12 * * *" && r.IsRecurring && r.IsDurable), It.IsAny<CancellationToken>()), Times.Once);

        await bridge.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RegisterBackgroundAgentCronTasksAsync_WhenAlreadyRegistered_Skips()
    {
        var taskStore = new Mock<ICronTaskStore>();
        var agentProvider = new Mock<IAgentDefinitionProvider>();
        agentProvider.Setup(a => a.GetAgentDefinitionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentDefinition>
            {
                new() { Role = AgentRole.Executor, Variant = ExecutorVariant.Doctor, IsBackground = true, WhenToUse = "维护关键词" }
            });
        taskStore.Setup(t => t.GetAllTasksAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CronTask>
        {
            new() { Id = "existing", CronExpression = "0 */12 * * *", Prompt = "executor:doctor", CreatedAt = 0 }
        });

        var goalEngine = new Mock<IGoalEngine>();
        var bridge = new CronGoalBridge(taskStore.Object, goalEngine.Object, agentProvider.Object);

        await bridge.StartAsync().ConfigureAwait(true);

        taskStore.Verify(t => t.AddTaskAsync(It.IsAny<CreateCronTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        await bridge.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RegisterBackgroundAgentCronTasksAsync_WhenProviderThrows_LogsAndContinues()
    {
        var logger = new Mock<ILogger<CronGoalBridge>>();
        var agentProvider = new Mock<IAgentDefinitionProvider>();
        agentProvider.Setup(a => a.GetAgentDefinitionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>())).ThrowsAsync(new Exception("fail"));

        var (_, goalEngine, bridge) = CreateBridge(agentProvider.Object, logger.Object);

        await bridge.StartAsync().ConfigureAwait(true);
        Assert.True(bridge.IsStarted);

        await bridge.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task RegisterBackgroundAgentCronTasksAsync_NonBackgroundAgent_IsIgnored()
    {
        var taskStore = new Mock<ICronTaskStore>();
        var agentProvider = new Mock<IAgentDefinitionProvider>();
        agentProvider.Setup(a => a.GetAgentDefinitionsAsync(It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AgentDefinition>
            {
                new() { Role = AgentRole.Executor, Variant = ExecutorVariant.Code, IsBackground = false, WhenToUse = "代码编辑" }
            });

        var goalEngine = new Mock<IGoalEngine>();
        var bridge = new CronGoalBridge(taskStore.Object, goalEngine.Object, agentProvider.Object);

        await bridge.StartAsync().ConfigureAwait(true);

        taskStore.Verify(t => t.AddTaskAsync(It.IsAny<CreateCronTaskRequest>(), It.IsAny<CancellationToken>()), Times.Never);

        await bridge.DisposeAsync().ConfigureAwait(true);
    }
}
