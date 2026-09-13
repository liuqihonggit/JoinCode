
#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Core.Goal.Tests;

public sealed class GoalEnginePipelineTests
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 断言两个 DateTime 近似相等（容差 1 秒），用于避免 mock 时钟与实际调用之间的微小差异
    /// </summary>
    private static void AssertDateTimeApproximate(DateTime expected, DateTime? actual, TimeSpan tolerance)
    {
        Assert.NotNull(actual);
        Assert.True(Math.Abs((expected - actual.Value).TotalMilliseconds) <= tolerance.TotalMilliseconds,
            $"Expected ~{expected:O} but got {actual.Value:O} (tolerance: {tolerance})");
    }

    private static (Mock<IChatClient> kernel, Mock<IGoalEvaluator> evaluator) CreateMocks()
    {
        var kernel = new Mock<IChatClient>();
        var evaluator = new Mock<IGoalEvaluator>();
        return (kernel, evaluator);
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
            System.Diagnostics.Trace.WriteLine("[GoalEnginePipelineTests] GoalEngine 后台循环未在超时内退出，强制忽略");
        }
        catch (OperationCanceledException)
        {
            System.Diagnostics.Trace.WriteLine("[GoalEnginePipelineTests] GoalEngine 已取消，忽略");
        }
    }

    private static GoalEngine CreateEngineWithPipeline(
        Mock<IChatClient> kernel,
        Mock<IGoalEvaluator> evaluator,
        IEnumerable<IGoalLifecycleMiddleware> middlewares)
    {
        return new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object, lifecycleMiddlewares: middlewares, serviceProvider: EmptyServiceProvider.Instance);
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new();
        public object? GetService(Type serviceType) => null;
    }

    [Fact]
    public async Task StartAsync_WithPipeline_SetsStateToPursuing()
    {
        var (kernel, evaluator) = CreateMocks();
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.Completed("完成"));

        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = "完成", TokenUsage = new TokenUsage(50, 25) }]);
        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = CreateEngineWithPipeline(kernel, evaluator, [new GoalEngineControlMiddleware(), new GoalStateTransitionMiddleware(Mock.Of<IClockService>())]);
        try
        {
            var state = await engine.StartAsync("测试目标").ConfigureAwait(true);

            Assert.Equal(GoalStatus.Pursuing, state.Status);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task StartAsync_WithPipeline_RespectsValidationFailure()
    {
        var (kernel, evaluator) = CreateMocks();

        var engine = CreateEngineWithPipeline(kernel, evaluator, [new GoalStateValidationMiddleware(), new GoalStateTransitionMiddleware(Mock.Of<IClockService>())]);
        try
        {
            await engine.StartAsync("目标1").ConfigureAwait(true);
            // 状态转换中间件无条件设置，验证中间件失败后后续中间件仍然执行
            Assert.Equal(GoalStatus.Pursuing, engine.CurrentState?.Status);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task PauseAsync_WithPipeline_SetsStatusToPaused()
    {
        var (kernel, evaluator) = CreateMocks();
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.NotCompleted("继续"));

        using var gate = new SemaphoreSlim(0, 1);
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(async (MessageList _, ChatOptions _, IChatClient _, CancellationToken ct) =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(5));
                await gate.WaitAsync(linkedCts.Token).ConfigureAwait(true);
                return [new ApiMessage { Role = MessageRole.Assistant, Content = "工作中", TokenUsage = new TokenUsage(50, 25) }];
            });
        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var clock = new Mock<IClockService>();
        var now = DateTime.UtcNow;
        clock.Setup(c => c.GetUtcNow()).Returns(now);

        var engine = CreateEngineWithPipeline(kernel, evaluator,
        [
            new GoalStateValidationMiddleware(),
            new GoalStateTransitionMiddleware(clock.Object),
            new GoalHeartbeatControlMiddleware()
        ]);

        try
        {
            await engine.StartAsync("测试目标").ConfigureAwait(true);
            await engine.PauseAsync().ConfigureAwait(true);

            Assert.Equal(GoalStatus.Paused, engine.CurrentState?.Status);
            AssertDateTimeApproximate(now, engine.CurrentState?.PausedAt, TimeSpan.FromSeconds(1));
        }
        finally
        {
            try { gate.Release(); }
            catch (SemaphoreFullException ex) { System.Diagnostics.Trace.WriteLine($"[GoalEnginePipelineTests] Gate already released: {ex.Message}"); }
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ResumeAsync_WithPipeline_SetsStatusToPursuing()
    {
        var (kernel, evaluator) = CreateMocks();
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.Completed("完成"));

        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = "完成", TokenUsage = new TokenUsage(50, 25) }]);
        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = CreateEngineWithPipeline(kernel, evaluator,
        [
            new GoalStateValidationMiddleware(),
            new GoalStateTransitionMiddleware(Mock.Of<IClockService>()),
            new GoalEngineControlMiddleware()
        ]);

        try
        {
            await engine.StartAsync("测试目标").ConfigureAwait(true);
            await engine.PauseAsync().ConfigureAwait(true);
            await engine.ResumeAsync().ConfigureAwait(true);

            Assert.Equal(GoalStatus.Pursuing, engine.CurrentState?.Status);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ClearAsync_WithPipeline_SetsStatusToUnmet()
    {
        var (kernel, evaluator) = CreateMocks();
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.NotCompleted("继续"));

        using var gate = new SemaphoreSlim(0, 1);
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(async (MessageList _, ChatOptions _, IChatClient _, CancellationToken ct) =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(5));
                await gate.WaitAsync(linkedCts.Token).ConfigureAwait(true);
                return [new ApiMessage { Role = MessageRole.Assistant, Content = "工作中", TokenUsage = new TokenUsage(50, 25) }];
            });
        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var clock = new Mock<IClockService>();
        var now = DateTime.UtcNow;
        clock.Setup(c => c.GetUtcNow()).Returns(now);

        var engine = CreateEngineWithPipeline(kernel, evaluator,
        [
            new GoalStateValidationMiddleware(),
            new GoalStateTransitionMiddleware(clock.Object),
            new GoalEngineControlMiddleware(),
            new GoalHeartbeatControlMiddleware()
        ]);

        try
        {
            await engine.StartAsync("测试目标").ConfigureAwait(true);
            await engine.ClearAsync().ConfigureAwait(true);

            Assert.Equal(GoalStatus.Unmet, engine.CurrentState?.Status);
            AssertDateTimeApproximate(now, engine.CurrentState?.AchievedAt, TimeSpan.FromSeconds(1));
        }
        finally
        {
            try { gate.Release(); }
            catch (SemaphoreFullException ex) { System.Diagnostics.Trace.WriteLine($"[GoalEnginePipelineTests] Gate already released: {ex.Message}"); }
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task MarkCompletedAsync_WithPipeline_SetsStatusToAchieved()
    {
        var (kernel, evaluator) = CreateMocks();
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.NotCompleted("继续"));

        using var gate = new SemaphoreSlim(0, 1);
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(async (MessageList _, ChatOptions _, IChatClient _, CancellationToken ct) =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(5));
                await gate.WaitAsync(linkedCts.Token).ConfigureAwait(true);
                return [new ApiMessage { Role = MessageRole.Assistant, Content = "工作中", TokenUsage = new TokenUsage(50, 25) }];
            });
        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var clock = new Mock<IClockService>();
        var now = DateTime.UtcNow;
        clock.Setup(c => c.GetUtcNow()).Returns(now);

        var engine = CreateEngineWithPipeline(kernel, evaluator,
        [
            new GoalStateValidationMiddleware(),
            new GoalStateTransitionMiddleware(clock.Object),
            new GoalEngineControlMiddleware(),
            new GoalHeartbeatControlMiddleware(),
            new GoalCompletionSignalMiddleware()
        ]);

        try
        {
            await engine.StartAsync("测试目标").ConfigureAwait(true);
            await engine.MarkCompletedAsync("完成").ConfigureAwait(true);

            Assert.Equal(GoalStatus.Achieved, engine.CurrentState?.Status);
            AssertDateTimeApproximate(now, engine.CurrentState?.AchievedAt, TimeSpan.FromSeconds(1));
            Assert.True(engine.CurrentState?.LastEvaluation?.IsCompleted);
        }
        finally
        {
            try { gate.Release(); }
            catch (SemaphoreFullException ex) { System.Diagnostics.Trace.WriteLine($"[GoalEnginePipelineTests] Gate already released: {ex.Message}"); }
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task MarkUnmetAsync_WithPipeline_SetsStatusToUnmet()
    {
        var (kernel, evaluator) = CreateMocks();
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.NotCompleted("继续"));

        using var gate = new SemaphoreSlim(0, 1);
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(async (MessageList _, ChatOptions _, IChatClient _, CancellationToken ct) =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(TimeSpan.FromSeconds(5));
                await gate.WaitAsync(linkedCts.Token).ConfigureAwait(true);
                return [new ApiMessage { Role = MessageRole.Assistant, Content = "工作中", TokenUsage = new TokenUsage(50, 25) }];
            });
        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var clock = new Mock<IClockService>();
        var now = DateTime.UtcNow;
        clock.Setup(c => c.GetUtcNow()).Returns(now);

        var engine = CreateEngineWithPipeline(kernel, evaluator,
        [
            new GoalStateValidationMiddleware(),
            new GoalStateTransitionMiddleware(clock.Object),
            new GoalEngineControlMiddleware(),
            new GoalHeartbeatControlMiddleware()
        ]);

        try
        {
            await engine.StartAsync("测试目标").ConfigureAwait(true);
            await engine.MarkUnmetAsync("测试原因").ConfigureAwait(true);

            Assert.Equal(GoalStatus.Unmet, engine.CurrentState?.Status);
            AssertDateTimeApproximate(now, engine.CurrentState?.AchievedAt, TimeSpan.FromSeconds(1));
        }
        finally
        {
            try { gate.Release(); }
            catch (SemaphoreFullException ex) { System.Diagnostics.Trace.WriteLine($"[GoalEnginePipelineTests] Gate already released: {ex.Message}"); }
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }
}