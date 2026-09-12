namespace Core.Tests.Query.StopHooks;

public class QueryStopHookManagerTests
{
    private readonly QueryStopHookManager _manager = new(NullLogger<QueryStopHookManager>.Instance);

    [Fact]
    public async Task ExecuteStopHooksAsync_NoHooks_ShouldReturnStop()
    {
        var result = await _manager.ExecuteStopHooksAsync("session-1", "timeout").ConfigureAwait(true);

        result.ShouldStop.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteStopHooksAsync_WithContinueHook_ShouldReturnStop()
    {
        var hook = CreateMockHook("hook-1", 10, StopHookResult.Continue());
        _manager.RegisterStopHook(hook.Object);

        var result = await _manager.ExecuteStopHooksAsync("session-1", "timeout").ConfigureAwait(true);

        result.ShouldStop.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteStopHooksAsync_WithStopHook_ShouldShortCircuit()
    {
        var stopHook = CreateMockHook("stop-hook", 5, StopHookResult.Stop("budget exceeded"));
        var continueHook = CreateMockHook("continue-hook", 10, StopHookResult.Continue());

        _manager.RegisterStopHook(stopHook.Object);
        _manager.RegisterStopHook(continueHook.Object);

        var result = await _manager.ExecuteStopHooksAsync("session-1", "timeout").ConfigureAwait(true);

        result.ShouldStop.Should().BeTrue();
        result.Message.Should().Be("budget exceeded");
        continueHook.Verify(h => h.OnStopAsync(It.IsAny<StopHookContext>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ExecuteStopHooksAsync_HookPriority_ShouldExecuteLowerPriorityFirst()
    {
        var executionOrder = new List<string>();
        var hook1 = CreateMockHook("hook-p1", 1, StopHookResult.Continue());
        var hook2 = CreateMockHook("hook-p5", 5, StopHookResult.Continue());
        var hook3 = CreateMockHook("hook-p10", 10, StopHookResult.Continue());

        hook1.Setup(h => h.OnStopAsync(It.IsAny<StopHookContext>(), It.IsAny<CancellationToken>()))
            .Callback<StopHookContext, CancellationToken>((_, _) => executionOrder.Add("hook-p1"))
            .ReturnsAsync(StopHookResult.Continue());
        hook2.Setup(h => h.OnStopAsync(It.IsAny<StopHookContext>(), It.IsAny<CancellationToken>()))
            .Callback<StopHookContext, CancellationToken>((_, _) => executionOrder.Add("hook-p5"))
            .ReturnsAsync(StopHookResult.Continue());
        hook3.Setup(h => h.OnStopAsync(It.IsAny<StopHookContext>(), It.IsAny<CancellationToken>()))
            .Callback<StopHookContext, CancellationToken>((_, _) => executionOrder.Add("hook-p10"))
            .ReturnsAsync(StopHookResult.Continue());

        _manager.RegisterStopHook(hook3.Object);
        _manager.RegisterStopHook(hook1.Object);
        _manager.RegisterStopHook(hook2.Object);

        await _manager.ExecuteStopHooksAsync("session-1", "timeout").ConfigureAwait(true);

        executionOrder.Should().Equal(["hook-p1", "hook-p5", "hook-p10"]);
    }

    [Fact]
    public void RegisterStopHook_NullHook_ShouldThrowArgumentNullException()
    {
        var act = () => _manager.RegisterStopHook(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void UnregisterStopHook_NullHookName_ShouldThrowArgumentNullException()
    {
        var act = () => _manager.UnregisterStopHook(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task UnregisterStopHook_ExistingHook_ShouldRemoveHook()
    {
        var hook = CreateMockHook("removable-hook", 1, StopHookResult.Stop("stopped"));
        _manager.RegisterStopHook(hook.Object);

        _manager.UnregisterStopHook("removable-hook");

        var result = await _manager.ExecuteStopHooksAsync("session-1", "timeout").ConfigureAwait(true);
        result.ShouldStop.Should().BeTrue();
        result.Message.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteStopHooksAsync_NullSessionId_ShouldThrowArgumentNullException()
    {
        var act = async () => await _manager.ExecuteStopHooksAsync(null!, "timeout").ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteStopHooksAsync_NullReason_ShouldThrowArgumentNullException()
    {
        var act = async () => await _manager.ExecuteStopHooksAsync("session-1", null!).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task ExecuteStopHooksAsync_HookThrowsException_ShouldContinueToNextHook()
    {
        var failingHook = CreateMockHook("failing-hook", 1, StopHookResult.Continue());
        failingHook.Setup(h => h.OnStopAsync(It.IsAny<StopHookContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hook failed"));

        var nextHook = CreateMockHook("next-hook", 5, StopHookResult.Stop("next stopped"));
        _manager.RegisterStopHook(failingHook.Object);
        _manager.RegisterStopHook(nextHook.Object);

        var result = await _manager.ExecuteStopHooksAsync("session-1", "timeout").ConfigureAwait(true);

        result.ShouldStop.Should().BeTrue();
        result.Message.Should().Be("next stopped");
    }

    [Fact]
    public void RegisterStopHook_DuplicateName_ShouldOverwrite()
    {
        var hook1 = CreateMockHook("same-name", 1, StopHookResult.Stop("first"));
        var hook2 = CreateMockHook("same-name", 5, StopHookResult.Continue("second"));

        _manager.RegisterStopHook(hook1.Object);
        _manager.RegisterStopHook(hook2.Object);

        hook2.Verify(h => h.Name, Times.AtLeastOnce());
    }

    private static Mock<IQueryStopHook> CreateMockHook(string name, int priority, StopHookResult result)
    {
        var mock = new Mock<IQueryStopHook>();
        mock.SetupGet(h => h.Name).Returns(name);
        mock.SetupGet(h => h.Priority).Returns(priority);
        mock.Setup(h => h.OnStopAsync(It.IsAny<StopHookContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock;
    }
}
