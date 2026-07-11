
namespace Core.Tests.Scheduling;

/// <summary>
/// ParallelExecutionEngine 单元测试类
/// 测试并行执行引擎的各种场景，包括构造函数注入和执行流程
/// </summary>
public class ParallelExecutionEngineTests
{
    private readonly Mock<IQueryEngine> _queryEngineMock;
    private readonly Mock<IAgentLifecycleManager> _lifecycleManagerMock;
    private readonly Mock<IAgentWorktreeManager> _worktreeManagerMock;
    private readonly Mock<IAgentMessageBroker> _messageBrokerMock;
    private readonly Mock<IAgentExecutionEngine> _executionEngineMock;

    public ParallelExecutionEngineTests()
    {
        _queryEngineMock = new Mock<IQueryEngine>();
        _lifecycleManagerMock = new Mock<IAgentLifecycleManager>();
        _worktreeManagerMock = new Mock<IAgentWorktreeManager>();
        _messageBrokerMock = new Mock<IAgentMessageBroker>();
        _executionEngineMock = new Mock<IAgentExecutionEngine>();
    }

    private AgentCoordinator CreateAgentCoordinator()
    {
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

        return new AgentCoordinator(
            new AgentCoreDependencies(
                _lifecycleManagerMock.Object,
                _worktreeManagerMock.Object,
                _messageBrokerMock.Object,
                _executionEngineMock.Object,
                new AgentStateMachine()),
            clock: JoinCode.Abstractions.Clock.SystemClockService.Instance,
            disposePipeline,
            spawnPipeline,
            logger: NullLogger<AgentCoordinator>.Instance);
    }

    #region 构造函数注入测试

    /// <summary>
    /// 测试使用 AgentCoordinator 构造引擎时，应正确初始化
    /// </summary>
    [Fact]
    public void Constructor_WithAgentCoordinator_ShouldInitializeCorrectly()
    {
        var agentCoordinator = CreateAgentCoordinator();

        var engine = new ParallelExecutionEngine(
            agentCoordinator,
            NullLogger<ParallelExecutionEngine>.Instance);

        engine.Should().NotBeNull();
    }

    /// <summary>
    /// 测试使用 null AgentCoordinator 构造引擎时，应抛出 ArgumentNullException
    /// </summary>
    [Fact]
    public void Constructor_WithNullAgentCoordinator_ShouldThrowArgumentNullException()
    {
        var act = () => new ParallelExecutionEngine(null!);

        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("agentCoordinator");
    }

    /// <summary>
    /// 测试使用模拟模式构造引擎时，应正确初始化
    /// </summary>
    [Fact]
    public void Constructor_SimulationMode_ShouldInitializeCorrectly()
    {
        var engine = new ParallelExecutionEngine(simulationMode: true, NullLogger<ParallelExecutionEngine>.Instance);

        engine.Should().NotBeNull();
    }

    /// <summary>
    /// 测试使用 null Logger 构造引擎时，应正确初始化（Logger 是可选的）
    /// </summary>
    [Fact]
    public void Constructor_WithNullLogger_ShouldInitializeCorrectly()
    {
        var agentCoordinator = CreateAgentCoordinator();

        var engine = new ParallelExecutionEngine(agentCoordinator, null);

        engine.Should().NotBeNull();
    }

    #endregion

    #region 执行测试

    /// <summary>
    /// 测试在模拟模式下执行时，应返回结果
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_SimulationMode_ShouldReturnResult()
    {
        // Arrange
        var engine = new ParallelExecutionEngine(simulationMode: true);

        // Act
        var result = await engine.ExecuteAsync().ConfigureAwait(true);

        // Assert
        result.Should().NotBeNull();
    }

    /// <summary>
    /// 测试执行选项可以正确传递
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_WithOptions_ShouldUseOptions()
    {
        // Arrange
        var engine = new ParallelExecutionEngine(simulationMode: true);
        var options = new ExecutionOptions
        {
            MaxConcurrentTasks = 5,
            SimulatedWorkDurationMs = 100
        };

        // Act
        var result = await engine.ExecuteAsync(options).ConfigureAwait(true);

        // Assert
        result.Should().NotBeNull();
    }

    #endregion
}

file sealed class ActionMiddleware<TContext>(Func<TContext, MiddlewareDelegate<TContext>, CancellationToken, Task> invoke) : IMiddleware<TContext>
{
    public Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct) => invoke(context, next, ct);
}
