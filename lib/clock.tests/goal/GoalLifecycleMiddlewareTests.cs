namespace Core.Goal.Tests;

public sealed class GoalLifecycleMiddlewareTests
{
    private static GoalLifecycleContext CreateContext(GoalOperation operation, GoalState? state = null, IToolPermissionManager? permissionManager = null, string? reason = null)
    {
        return new GoalLifecycleContext
        {
            Operation = operation,
            State = state ?? new GoalState { GoalId = "g1", Objective = "test", Status = GoalStatus.Pursuing },
            ChatHistory = new MessageList(),
            Heartbeat = new Mock<IGoalHeartbeat>().Object,
            PermissionManager = permissionManager,
            Reason = reason
        };
    }

    private static MiddlewareDelegate<GoalLifecycleContext> Next => (ctx, ct) => Task.CompletedTask;

    [Theory]
    [InlineData(GoalOperation.MarkCompleted)]
    [InlineData(GoalOperation.MarkUnmet)]
    public async Task GoalCompletionSignalMiddleware_SetsShouldSignalCompletion(GoalOperation operation)
    {
        var middleware = new GoalCompletionSignalMiddleware();
        var ctx = CreateContext(operation);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.True(ctx.ShouldSignalCompletion);
    }

    [Theory]
    [InlineData(GoalOperation.Start)]
    [InlineData(GoalOperation.Pause)]
    [InlineData(GoalOperation.Resume)]
    [InlineData(GoalOperation.Clear)]
    public async Task GoalCompletionSignalMiddleware_DoesNotSetSignalForOtherOperations(GoalOperation operation)
    {
        var middleware = new GoalCompletionSignalMiddleware();
        var ctx = CreateContext(operation);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.False(ctx.ShouldSignalCompletion);
    }

    [Theory]
    [InlineData(GoalOperation.Start)]
    [InlineData(GoalOperation.Resume)]
    public async Task GoalEngineControlMiddleware_SetsShouldStartEngineLoop(GoalOperation operation)
    {
        var middleware = new GoalEngineControlMiddleware();
        var ctx = CreateContext(operation);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.True(ctx.ShouldStartEngineLoop);
        Assert.False(ctx.ShouldCancelEngineLoop);
    }

    [Theory]
    [InlineData(GoalOperation.Clear)]
    [InlineData(GoalOperation.MarkCompleted)]
    [InlineData(GoalOperation.MarkUnmet)]
    public async Task GoalEngineControlMiddleware_SetsShouldCancelEngineLoop(GoalOperation operation)
    {
        var middleware = new GoalEngineControlMiddleware();
        var ctx = CreateContext(operation);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.True(ctx.ShouldCancelEngineLoop);
        Assert.False(ctx.ShouldStartEngineLoop);
    }

    [Theory]
    [InlineData(GoalOperation.Pause)]
    [InlineData(GoalOperation.Clear)]
    [InlineData(GoalOperation.MarkCompleted)]
    [InlineData(GoalOperation.MarkUnmet)]
    public async Task GoalHeartbeatControlMiddleware_SetsShouldResetHeartbeat(GoalOperation operation)
    {
        var middleware = new GoalHeartbeatControlMiddleware();
        var ctx = CreateContext(operation);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.True(ctx.ShouldResetHeartbeat);
    }

    [Theory]
    [InlineData(GoalOperation.Start)]
    [InlineData(GoalOperation.Resume)]
    public async Task GoalHeartbeatControlMiddleware_DoesNotSetResetForOtherOperations(GoalOperation operation)
    {
        var middleware = new GoalHeartbeatControlMiddleware();
        var ctx = CreateContext(operation);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.False(ctx.ShouldResetHeartbeat);
    }

    [Fact]
    public async Task GoalPermissionModeMiddleware_WithoutPermissionManager_CallsNext()
    {
        var middleware = new GoalPermissionModeMiddleware();
        var ctx = CreateContext(GoalOperation.Start);
        var nextCalled = false;

        await middleware.InvokeAsync(ctx, (c, ct) =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        }, CancellationToken.None).ConfigureAwait(true);

        Assert.True(nextCalled);
    }

    [Fact]
    public async Task GoalPermissionModeMiddleware_Start_SavesAndSetsAutoMode()
    {
        var middleware = new GoalPermissionModeMiddleware();
        var permissionManager = new Mock<IToolPermissionManager>();
        permissionManager.Setup(x => x.GetCurrentModeAsync(It.IsAny<CancellationToken>())).ReturnsAsync(PermissionMode.Ask);
        permissionManager.Setup(x => x.SetPermissionModeAsync(PermissionMode.Auto, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var ctx = CreateContext(GoalOperation.Start, permissionManager: permissionManager.Object);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(PermissionMode.Ask, ctx.SavedPermissionMode);
        permissionManager.Verify(x => x.SetPermissionModeAsync(PermissionMode.Auto, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GoalPermissionModeMiddleware_Start_WhenGetModeThrows_SetsSavedModeToNull()
    {
        var middleware = new GoalPermissionModeMiddleware();
        var permissionManager = new Mock<IToolPermissionManager>();
        permissionManager.Setup(x => x.GetCurrentModeAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("fail"));
        var ctx = CreateContext(GoalOperation.Start, permissionManager: permissionManager.Object);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.Null(ctx.SavedPermissionMode);
    }

    [Theory]
    [InlineData(GoalOperation.Clear)]
    [InlineData(GoalOperation.MarkCompleted)]
    [InlineData(GoalOperation.MarkUnmet)]
    public async Task GoalPermissionModeMiddleware_Restore_RestoresSavedMode(GoalOperation operation)
    {
        var middleware = new GoalPermissionModeMiddleware();
        var permissionManager = new Mock<IToolPermissionManager>();
        permissionManager.Setup(x => x.SetPermissionModeAsync(PermissionMode.Ask, It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var ctx = CreateContext(operation, permissionManager: permissionManager.Object);
        ctx.SavedPermissionMode = PermissionMode.Ask;

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        permissionManager.Verify(x => x.SetPermissionModeAsync(PermissionMode.Ask, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(GoalOperation.Clear)]
    [InlineData(GoalOperation.MarkCompleted)]
    [InlineData(GoalOperation.MarkUnmet)]
    public async Task GoalPermissionModeMiddleware_Restore_WhenNoSavedMode_DoesNotCallSetMode(GoalOperation operation)
    {
        var middleware = new GoalPermissionModeMiddleware();
        var permissionManager = new Mock<IToolPermissionManager>();
        var ctx = CreateContext(operation, permissionManager: permissionManager.Object);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        permissionManager.Verify(x => x.SetPermissionModeAsync(It.IsAny<PermissionMode>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GoalPermissionModeMiddleware_Restore_WhenSetModeThrows_DoesNotThrow()
    {
        var middleware = new GoalPermissionModeMiddleware();
        var permissionManager = new Mock<IToolPermissionManager>();
        permissionManager.Setup(x => x.SetPermissionModeAsync(It.IsAny<PermissionMode>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("fail"));
        var ctx = CreateContext(GoalOperation.Clear, permissionManager: permissionManager.Object);
        ctx.SavedPermissionMode = PermissionMode.Ask;

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.True(true);
    }

    [Theory]
    [InlineData(GoalOperation.Start, GoalStatus.Unmet, GoalStatus.Pursuing)]
    [InlineData(GoalOperation.Pause, GoalStatus.Pursuing, GoalStatus.Paused)]
    [InlineData(GoalOperation.Resume, GoalStatus.Paused, GoalStatus.Pursuing)]
    [InlineData(GoalOperation.Clear, GoalStatus.Pursuing, GoalStatus.Unmet)]
    [InlineData(GoalOperation.MarkCompleted, GoalStatus.Pursuing, GoalStatus.Achieved)]
    [InlineData(GoalOperation.MarkUnmet, GoalStatus.Pursuing, GoalStatus.Unmet)]
    public async Task GoalStateTransitionMiddleware_TransitionsStatus(GoalOperation operation, GoalStatus initialStatus, GoalStatus expectedStatus)
    {
        var clock = new Mock<IClockService>();
        clock.Setup(x => x.GetUtcNow()).Returns(DateTime.UtcNow);
        var middleware = new GoalStateTransitionMiddleware(clock.Object);
        var state = new GoalState { GoalId = "g1", Objective = "test", Status = initialStatus };
        var ctx = CreateContext(operation, state: state);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(expectedStatus, ctx.State.Status);
        Assert.True(ctx.StateTransitioned);
    }

    [Fact]
    public async Task GoalStateTransitionMiddleware_Pause_SetsPausedAt()
    {
        var clock = new Mock<IClockService>();
        var now = DateTime.UtcNow;
        clock.Setup(x => x.GetUtcNow()).Returns(now);
        var middleware = new GoalStateTransitionMiddleware(clock.Object);
        var state = new GoalState { GoalId = "g1", Objective = "test", Status = GoalStatus.Pursuing };
        var ctx = CreateContext(GoalOperation.Pause, state: state);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(now, ctx.State.PausedAt);
    }

    [Fact]
    public async Task GoalStateTransitionMiddleware_Resume_ClearsPausedAt()
    {
        var clock = new Mock<IClockService>();
        var middleware = new GoalStateTransitionMiddleware(clock.Object);
        var state = new GoalState { GoalId = "g1", Objective = "test", Status = GoalStatus.Paused, PausedAt = DateTime.UtcNow };
        var ctx = CreateContext(GoalOperation.Resume, state: state);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.Null(ctx.State.PausedAt);
    }

    [Fact]
    public async Task GoalStateTransitionMiddleware_MarkCompleted_SetsAchievedAtAndEvaluation()
    {
        var clock = new Mock<IClockService>();
        var now = DateTime.UtcNow;
        clock.Setup(x => x.GetUtcNow()).Returns(now);
        var middleware = new GoalStateTransitionMiddleware(clock.Object);
        var state = new GoalState { GoalId = "g1", Objective = "test", Status = GoalStatus.Pursuing };
        var ctx = CreateContext(GoalOperation.MarkCompleted, state: state, reason: "done");

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(now, ctx.State.AchievedAt);
        Assert.NotNull(ctx.State.LastEvaluation);
        Assert.True(ctx.State.LastEvaluation.IsCompleted);
        Assert.Equal("done", ctx.State.LastEvaluation.Reason);
    }

    [Fact]
    public async Task GoalStateTransitionMiddleware_MarkUnmet_SetsAchievedAtAndEvaluation()
    {
        var clock = new Mock<IClockService>();
        var now = DateTime.UtcNow;
        clock.Setup(x => x.GetUtcNow()).Returns(now);
        var middleware = new GoalStateTransitionMiddleware(clock.Object);
        var state = new GoalState { GoalId = "g1", Objective = "test", Status = GoalStatus.Pursuing };
        var ctx = CreateContext(GoalOperation.MarkUnmet, state: state, reason: "not done");

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(now, ctx.State.AchievedAt);
        Assert.NotNull(ctx.State.LastEvaluation);
        Assert.False(ctx.State.LastEvaluation.IsCompleted);
        Assert.Equal("not done", ctx.State.LastEvaluation.Reason);
    }

    [Fact]
    public async Task GoalStateTransitionMiddleware_MarkCompleted_WithoutReason_UsesDefault()
    {
        var clock = new Mock<IClockService>();
        clock.Setup(x => x.GetUtcNow()).Returns(DateTime.UtcNow);
        var middleware = new GoalStateTransitionMiddleware(clock.Object);
        var state = new GoalState { GoalId = "g1", Objective = "test", Status = GoalStatus.Pursuing };
        var ctx = CreateContext(GoalOperation.MarkCompleted, state: state);

        await middleware.InvokeAsync(ctx, Next, CancellationToken.None).ConfigureAwait(true);

        Assert.NotNull(ctx.State.LastEvaluation!);
        Assert.Equal("Completed", ctx.State.LastEvaluation!.Reason);
    }

    [Theory]
    [InlineData(GoalOperation.Start, GoalStatus.Pursuing, false)]
    [InlineData(GoalOperation.Start, GoalStatus.Paused, true)]
    [InlineData(GoalOperation.Pause, GoalStatus.Pursuing, true)]
    [InlineData(GoalOperation.Pause, GoalStatus.Paused, false)]
    [InlineData(GoalOperation.Resume, GoalStatus.Paused, true)]
    [InlineData(GoalOperation.Resume, GoalStatus.Pursuing, false)]
    [InlineData(GoalOperation.Clear, GoalStatus.Unmet, false)]
    [InlineData(GoalOperation.Clear, GoalStatus.Unmet, true, "g1")]
    [InlineData(GoalOperation.MarkCompleted, GoalStatus.Pursuing, true)]
    [InlineData(GoalOperation.MarkCompleted, GoalStatus.Paused, false)]
    [InlineData(GoalOperation.MarkUnmet, GoalStatus.Pursuing, true)]
    [InlineData(GoalOperation.MarkUnmet, GoalStatus.Paused, false)]
    public async Task GoalStateValidationMiddleware_ValidatesOperations(GoalOperation operation, GoalStatus status, bool expectedValid, string? goalId = null)
    {
        var middleware = new GoalStateValidationMiddleware();
        var state = new GoalState { GoalId = goalId ?? string.Empty, Objective = "test", Status = status };
        var ctx = CreateContext(operation, state: state);

        await middleware.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        if (expectedValid)
        {
            Assert.False(ctx.Failed);
        }
        else
        {
            Assert.True(ctx.Failed);
        }
    }
}
