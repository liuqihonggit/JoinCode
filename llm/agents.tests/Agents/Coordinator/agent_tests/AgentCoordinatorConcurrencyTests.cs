// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Tests.Agents.Coordinator;

/// <summary>
/// AgentCoordinator 并发安全测试 — 验证 UpdateExecutionContext CAS 循环消除 Lost Update
/// </summary>
public class AgentCoordinatorConcurrencyTests {
    private readonly Mock<IQueryEngine> _queryEngineMock;
    private readonly Mock<IAgentLifecycleManager> _lifecycleManagerMock;
    private readonly Mock<IAgentWorktreeManager> _worktreeManagerMock;
    private readonly Mock<IMailbox> _messageBrokerMock;
    private readonly Mock<IAgentExecutionEngine> _executionEngineMock;
    private readonly AgentCoordinator _coordinator;

    public AgentCoordinatorConcurrencyTests() {
        _queryEngineMock = new Mock<IQueryEngine>();
        _lifecycleManagerMock = new Mock<IAgentLifecycleManager>();
        _worktreeManagerMock = new Mock<IAgentWorktreeManager>();
        _messageBrokerMock = new Mock<IMailbox>();
        _executionEngineMock = new Mock<IAgentExecutionEngine>();

        var spawnPipeline = new MiddlewarePipeline<UnifiedSpawnContext>(
            [new ActionMiddleware<UnifiedSpawnContext>(async (ctx, next, ct) => {
                ctx.Agent = await _lifecycleManagerMock.Object.SpawnSubAgentAsync(ctx.Task, ctx.SubOptions, ct);
                ctx.ExecutionContext = new AgentExecutionContext {
                    AgentId = ctx.AgentId,
                    Task = ctx.Task,
                    SpawnedAt = JoinCode.Abstractions.Clock.SystemClockService.Instance.GetUtcNow(),
                    RetryCount = 0
                };
                await next(ctx, ct);
            })], onError: (_, _) => { });

        var disposePipeline = new MiddlewarePipeline<AgentDisposeContext>(
            [new ActionMiddleware<AgentDisposeContext>(async (ctx, next, ct) => {
                await _lifecycleManagerMock.Object.DisposeAgentAsync(ctx.AgentId, ct);
                _messageBrokerMock.Object.UnregisterAgent(ctx.AgentId);
                if (_worktreeManagerMock.Object.IsWorktreeIsolationEnabled) {
                    await _worktreeManagerMock.Object.CleanupWorktreeAsync(ctx.AgentId, cancellationToken: ct);
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

    /// <summary>
    /// 并发 N 线程同时 RetryCount++ → 最终 RetryCount 必须等于 N（无 Lost Update）
    /// </summary>
    [Theory]
    [InlineData(4, 1000)]
    [InlineData(8, 500)]
    [InlineData(16, 250)]
    public async Task UpdateExecutionContext_ConcurrentRetryCountIncrement_NoLostUpdate(int threadCount, int incrementsPerThread) {
        await using var agent = new AgentBase("concurrent-test", null, _queryEngineMock.Object, null);
        _lifecycleManagerMock.Setup(x => x.SpawnSubAgentAsync("concurrent-test", null, default))
            .ReturnsAsync(agent);

        var spawned = await _coordinator.SpawnSubAgentAsync("concurrent-test").ConfigureAwait(true);
        var agentId = spawned.ObjectId.UniqueId;

        using var barrier = new Barrier(threadCount);
        var tasks = new Task[threadCount];
        for (var t = 0; t < threadCount; t++) {
            tasks[t] = Task.Run(() => {
                barrier.SignalAndWait();
                for (var i = 0; i < incrementsPerThread; i++) {
                    _coordinator.UpdateExecutionContext(agentId, ctx => ctx with { RetryCount = ctx.RetryCount + 1 });
                }
            });
        }
        await Task.WhenAll(tasks).ConfigureAwait(true);

        var finalContext = _coordinator.GetExecutionContext(agentId);
        finalContext.Should().NotBeNull();
        var expected = threadCount * incrementsPerThread;
        finalContext!.RetryCount.Should().Be(expected,
            $"because {threadCount} threads each incremented RetryCount {incrementsPerThread} times via CAS RMW loop");
    }

    /// <summary>
    /// 并发混合更新：RetryCount++ 与 Outcome=Cancelled 交替 → 两者都必须保留（无覆盖）
    /// </summary>
    [Fact]
    public async Task UpdateExecutionContext_ConcurrentMixedUpdates_BothFieldsPreserved() {
        await using var agent = new AgentBase("mixed-concurrent", null, _queryEngineMock.Object, null);
        _lifecycleManagerMock.Setup(x => x.SpawnSubAgentAsync("mixed-concurrent", null, default))
            .ReturnsAsync(agent);

        var spawned = await _coordinator.SpawnSubAgentAsync("mixed-concurrent").ConfigureAwait(true);
        var agentId = spawned.ObjectId.UniqueId;

        const int retryThreads = 8;
        const int retryPerThread = 500;
        using var barrier = new Barrier(retryThreads + 1);

        var retryTasks = new Task[retryThreads];
        for (var t = 0; t < retryThreads; t++) {
            retryTasks[t] = Task.Run(() => {
                barrier.SignalAndWait();
                for (var i = 0; i < retryPerThread; i++) {
                    _coordinator.UpdateExecutionContext(agentId, ctx => ctx with { RetryCount = ctx.RetryCount + 1 });
                }
            });
        }

        var cancelTask = Task.Run(() => {
            barrier.SignalAndWait();
            for (var i = 0; i < 100; i++) {
                _coordinator.UpdateExecutionContext(agentId, ctx => ctx with { Outcome = AgentOutcome.Cancelled });
            }
        });

        await Task.WhenAll(retryTasks).ConfigureAwait(true);
        await cancelTask.ConfigureAwait(true);

        var finalContext = _coordinator.GetExecutionContext(agentId);
        finalContext.Should().NotBeNull();
        finalContext!.RetryCount.Should().Be(retryThreads * retryPerThread,
            "because RetryCount increments must not be lost by interleaved Outcome updates");
        finalContext.Outcome.Should().Be(AgentOutcome.Cancelled,
            "because the last Outcome update was Cancelled");
    }
}

sealed file class ActionMiddleware<TContext>(Func<TContext, MiddlewareDelegate<TContext>, CancellationToken, Task> invoke) : IMiddleware<TContext> {
    public Task InvokeAsync(TContext context, MiddlewareDelegate<TContext> next, CancellationToken ct) => invoke(context, next, ct);
}
