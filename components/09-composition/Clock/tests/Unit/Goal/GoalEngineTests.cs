
#pragma warning disable JCC3010, JCC3011, JCC3012
namespace Core.Goal.Tests;

public sealed class GoalEngineTests
{
    private static readonly TimeSpan DisposeTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan GateTimeout = TimeSpan.FromSeconds(5);

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
        var (kernel, evaluator) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);

        Assert.NotNull(engine);
        Assert.False(engine.IsRunning);
        Assert.Null(engine.CurrentState);
    }

    [Fact]
    public void Constructor_WithPermissionManager_Should_Create_Successfully()
    {
        var (kernel, evaluator) = CreateMocks();
        var permissionManager = new Mock<IToolPermissionManager>();

        var engine = new GoalEngine(kernel.Object, evaluator.Object, permissionManager: permissionManager.Object, heartbeat: CreateHeartbeatMock().Object);

        Assert.NotNull(engine);
        Assert.False(engine.IsRunning);
    }

    [Fact]
    public async Task StartAsync_Should_Set_State_To_Pursuing()
    {
        var (kernel, evaluator) = CreateMocks();

        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.Completed("目标已完成"));

        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = "工作完成", TokenUsage = new TokenUsage(100, 50) }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
        try
        {
            var state = await engine.StartAsync("实现用户注册功能").ConfigureAwait(true);

            Assert.NotNull(state);
            Assert.Equal(GoalStatus.Pursuing, state.Status);
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
        var (kernel, evaluator) = CreateMocks();

        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.Completed("目标已完成"));

        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = "完成", TokenUsage = new TokenUsage(100, 50) }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
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
        var (kernel, evaluator) = CreateMocks();

        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.NotCompleted("继续工作"));

        // 使用 SemaphoreSlim 替代 Task.Delay 反模式：阻塞 mock 调用直到测试完成断言
        using var gate = new SemaphoreSlim(0, 1);
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(async (MessageList _, ChatOptions _, IChatClient _, CancellationToken ct) =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(GateTimeout);
                await gate.WaitAsync(linkedCts.Token).ConfigureAwait(true);
                return [new ApiMessage { Role = MessageRole.Assistant, Content = "工作中", TokenUsage = new TokenUsage(100, 50) }];
            });

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
        try
        {
            await engine.StartAsync("目标1").ConfigureAwait(true);

            await Assert.ThrowsAsync<InvalidOperationException>(() => engine.StartAsync("目标2")).ConfigureAwait(true);

            await engine.ClearAsync().ConfigureAwait(true);
        }
        finally
        {
            try { gate.Release(); } catch (SemaphoreFullException) { System.Diagnostics.Trace.WriteLine("[GoalEngineTests] Semaphore already at max"); }
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task StartAsync_NullObjective_Should_Throw()
    {
        var (kernel, evaluator) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
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
        var (kernel, evaluator) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
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
        var (kernel, evaluator) = CreateMocks();

        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.NotCompleted("继续"));

        // 使用 SemaphoreSlim 替代 Task.Delay 反模式：阻塞 mock 调用直到 PauseAsync 被调用
        using var gate = new SemaphoreSlim(0, 1);
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(async (MessageList _, ChatOptions _, IChatClient _, CancellationToken ct) =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(GateTimeout);
                await gate.WaitAsync(linkedCts.Token).ConfigureAwait(true);
                return [new ApiMessage { Role = MessageRole.Assistant, Content = "工作中", TokenUsage = new TokenUsage(100, 50) }];
            });

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
        try
        {
            await engine.StartAsync("测试目标").ConfigureAwait(true);

            await engine.PauseAsync().ConfigureAwait(true);

            Assert.Equal(GoalStatus.Paused, engine.CurrentState?.Status);
            Assert.NotNull(engine.CurrentState?.PausedAt);
        }
        finally
        {
            try { gate.Release(); } catch (SemaphoreFullException) { System.Diagnostics.Trace.WriteLine("[GoalEngineTests] Semaphore already at max"); }
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ClearAsync_Should_Set_Status_To_Unmet()
    {
        var (kernel, evaluator) = CreateMocks();

        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.NotCompleted("继续"));

        // 使用 SemaphoreSlim 替代 Task.Delay 反模式：阻塞 mock 调用直到 ClearAsync 被调用
        using var gate = new SemaphoreSlim(0, 1);
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(async (MessageList _, ChatOptions _, IChatClient _, CancellationToken ct) =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(GateTimeout);
                await gate.WaitAsync(linkedCts.Token).ConfigureAwait(true);
                return [new ApiMessage { Role = MessageRole.Assistant, Content = "工作中", TokenUsage = new TokenUsage(100, 50) }];
            });

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
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
            try { gate.Release(); } catch (SemaphoreFullException) { System.Diagnostics.Trace.WriteLine("[GoalEngineTests] Semaphore already at max"); }
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task GoalLoop_WhenEvaluatorReturnsCompleted_Should_Set_Achieved()
    {
        var (kernel, evaluator) = CreateMocks();

        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.Completed("所有功能已实现并测试通过"));

        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = "工作完成", TokenUsage = new TokenUsage(100, 50) }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
        try
        {
            var state = await engine.StartAsync("实现功能").ConfigureAwait(true);

            await engine.WaitForCompletionAsync().WaitAsync(DisposeTimeout).ConfigureAwait(true);

            Assert.Equal(GoalStatus.Achieved, engine.CurrentState?.Status);
            Assert.NotNull(engine.CurrentState?.AchievedAt);
            Assert.Equal(1, engine.CurrentState?.TurnsCompleted);
            Assert.NotNull(engine.CurrentState?.LastEvaluation);
            Assert.True(engine.CurrentState?.LastEvaluation?.IsCompleted);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task DisposeAsync_Should_Not_Throw()
    {
        var (kernel, evaluator) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);

        await SafeDisposeAsync(engine).ConfigureAwait(true);
    }

    [Fact]
    public async Task GoalLoop_MultiTurn_Should_Loop_Until_Completed()
    {
        var (kernel, evaluator) = CreateMocks();

        var evalCallCount = 0;
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                evalCallCount++;
                return evalCallCount >= 3
                    ? GoalEvaluationResult.Completed("目标已完成")
                    : GoalEvaluationResult.NotCompleted($"第{evalCallCount}轮，仍在工作");
            });

        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = "工作中", TokenUsage = new TokenUsage(100, 50) }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
        try
        {
            await engine.StartAsync("多轮目标").ConfigureAwait(true);

            await engine.WaitForCompletionAsync().WaitAsync(DisposeTimeout).ConfigureAwait(true);

            Assert.Equal(GoalStatus.Achieved, engine.CurrentState?.Status);
            Assert.True(engine.CurrentState?.TurnsCompleted >= 3);
            Assert.NotNull(engine.CurrentState?.LastEvaluation);
            Assert.True(engine.CurrentState?.LastEvaluation?.IsCompleted);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task GoalLoop_BudgetLimited_Should_Stop_When_Budget_Exceeded()
    {
        var (kernel, evaluator) = CreateMocks();

        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.NotCompleted("继续工作"));

        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = "工作中", TokenUsage = new TokenUsage(100, 50) }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
        try
        {
            await engine.StartAsync("预算测试", tokenBudget: 100).ConfigureAwait(true);

            await engine.WaitForCompletionAsync().WaitAsync(DisposeTimeout).ConfigureAwait(true);

            Assert.Equal(GoalStatus.BudgetLimited, engine.CurrentState?.Status);
            Assert.True(engine.CurrentState?.TokensUsed >= 100);
        }
        finally
        {
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task PauseAndResume_Should_Work()
    {
        var (kernel, evaluator) = CreateMocks();

        var evalCallCount = 0;
        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                evalCallCount++;
                return GoalEvaluationResult.NotCompleted($"第{evalCallCount}轮");
            });

        // 使用 SemaphoreSlim 替代 Task.Delay 反模式：阻塞 mock 调用直到测试控制释放
        using var gate = new SemaphoreSlim(0, 1);
        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .Returns(async (MessageList _, ChatOptions _, IChatClient _, CancellationToken ct) =>
            {
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                linkedCts.CancelAfter(GateTimeout);
                await gate.WaitAsync(linkedCts.Token).ConfigureAwait(true);
                return [new ApiMessage { Role = MessageRole.Assistant, Content = "工作中", TokenUsage = new TokenUsage(50, 25) }];
            });

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
        try
        {
            await engine.StartAsync("暂停恢复测试").ConfigureAwait(true);

            await engine.PauseAsync().ConfigureAwait(true);

            // PauseAsync 不取消 _engineCts，需主动释放 gate 使循环检测到 Paused 状态后退出
            gate.Release();
            await engine.WaitForCompletionAsync().WaitAsync(DisposeTimeout).ConfigureAwait(true);

            Assert.Equal(GoalStatus.Paused, engine.CurrentState?.Status);
            var turnsBeforePause = engine.CurrentState?.TurnsCompleted ?? 0;

            await engine.ResumeAsync().ConfigureAwait(true);
            Assert.Equal(GoalStatus.Pursuing, engine.CurrentState?.Status);

            await engine.ClearAsync().ConfigureAwait(true);
            // ClearAsync 取消 _engineCts，mock 回调会因 ct 取消而退出
            await engine.WaitForCompletionAsync().WaitAsync(DisposeTimeout).ConfigureAwait(true);

            Assert.True(engine.CurrentState?.TurnsCompleted >= turnsBeforePause);
        }
        finally
        {
            try { gate.Release(); } catch (SemaphoreFullException) { System.Diagnostics.Trace.WriteLine("[GoalEngineTests] Semaphore already at max"); }
            await SafeDisposeAsync(engine).ConfigureAwait(true);
        }
    }

    [Fact]
    public async Task ClearAsync_When_No_Active_Goal_Should_Not_Throw()
    {
        var (kernel, evaluator) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
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
        var (kernel, evaluator) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
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
        var (kernel, evaluator) = CreateMocks();
        var engine = new GoalEngine(kernel.Object, evaluator.Object, heartbeat: CreateHeartbeatMock().Object);
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
        var (kernel, evaluator) = CreateMocks();
        var permissionManager = new Mock<IToolPermissionManager>();

        permissionManager.Setup(x => x.GetCurrentModeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(PermissionMode.Ask);
        permissionManager.Setup(x => x.SetPermissionModeAsync(It.IsAny<PermissionMode>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        evaluator.Setup(x => x.EvaluateAsync(It.IsAny<string>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GoalEvaluationResult.Completed("完成"));

        var chatService = new Mock<IQueryService>();
        chatService.Setup(x => x.GetApiMessageContentsAsync(It.IsAny<MessageList>(), It.IsAny<ChatOptions>(), It.IsAny<IChatClient>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ApiMessage { Role = MessageRole.Assistant, Content = "完成", TokenUsage = new TokenUsage(50, 25) }]);

        kernel.Setup(x => x.GetChatCompletionService()).Returns(chatService.Object);

        var engine = new GoalEngine(kernel.Object, evaluator.Object, permissionManager: permissionManager.Object, heartbeat: CreateHeartbeatMock().Object);
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
}
#pragma warning restore JCC3010, JCC3011, JCC3012
