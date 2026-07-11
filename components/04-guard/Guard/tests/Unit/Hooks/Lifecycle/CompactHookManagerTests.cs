namespace Guard.Tests.Hooks.Lifecycle;

public sealed class CompactHookManagerTests
{
    private readonly Mock<IHookOrchestrator> _orchestratorMock;
    private readonly CompactHookManager _sut;

    public CompactHookManagerTests()
    {
        _orchestratorMock = new Mock<IHookOrchestrator>();
        _sut = new CompactHookManager(_orchestratorMock.Object, NullLogger<CompactHookManager>.Instance);
    }

    [Fact]
    public void Constructor_NullOrchestrator_ShouldThrowArgumentNullException()
    {
        var act = () => new CompactHookManager(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task OnPreCompactAsync_NoHooks_ShouldReturnDefaultResult()
    {
        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.PreCompact,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableEmpty<HookResult>());

        var context = new CompactHookContext
        {
            SessionId = "session-1",
            Trigger = "manual",
            CurrentTokenCount = 10000,
            TargetTokenCount = 5000
        };

        var result = await _sut.OnPreCompactAsync(context).ConfigureAwait(true);

        result.ShouldCompact.Should().BeTrue();
        result.Action.Should().Be(CompactHookAction.Proceed);
        result.Message.Should().BeNull();
    }

    [Fact]
    public async Task OnPreCompactAsync_BlockingHook_ShouldReturnSkipAction()
    {
        var blockingResult = new HookResult
        {
            Outcome = HookOutcome.Blocking,
            Message = "Compression blocked by policy"
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.PreCompact,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { blockingResult }.ToAsyncEnumerable());

        var context = new CompactHookContext
        {
            SessionId = "session-1",
            Trigger = "auto",
            CurrentTokenCount = 10000,
            TargetTokenCount = 5000
        };

        var result = await _sut.OnPreCompactAsync(context).ConfigureAwait(true);

        result.ShouldCompact.Should().BeFalse();
        result.Action.Should().Be(CompactHookAction.Skip);
        result.Message.Should().Be("Compression blocked by policy");
    }

    [Fact]
    public async Task OnPreCompactAsync_PreventContinuation_ShouldReturnDeferAction()
    {
        var deferResult = new HookResult
        {
            Outcome = HookOutcome.Success,
            PreventContinuation = true,
            Message = "Defer compression"
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.PreCompact,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { deferResult }.ToAsyncEnumerable());

        var context = new CompactHookContext
        {
            SessionId = "session-1",
            Trigger = "manual",
            CurrentTokenCount = 8000,
            TargetTokenCount = 4000
        };

        var result = await _sut.OnPreCompactAsync(context).ConfigureAwait(true);

        result.ShouldCompact.Should().BeFalse();
        result.Action.Should().Be(CompactHookAction.Defer);
        result.Message.Should().Be("Defer compression");
    }

    [Fact]
    public async Task OnPreCompactAsync_UpdatedInputWithCustomAction_ShouldReturnCustomAction()
    {
        var customResult = new HookResult
        {
            Outcome = HookOutcome.Success,
            UpdatedInput = new Dictionary<string, JsonElement> { ["action"] = JsonElementHelper.FromString("Custom") }
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.PreCompact,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { customResult }.ToAsyncEnumerable());

        var context = new CompactHookContext
        {
            SessionId = "session-1",
            Trigger = "auto",
            CurrentTokenCount = 10000,
            TargetTokenCount = 5000
        };

        var result = await _sut.OnPreCompactAsync(context).ConfigureAwait(true);

        result.Action.Should().Be(CompactHookAction.Custom);
        result.ShouldCompact.Should().BeFalse();
    }

    [Fact]
    public async Task OnPreCompactAsync_UpdatedInputWithProceedAction_ShouldReturnProceed()
    {
        var proceedResult = new HookResult
        {
            Outcome = HookOutcome.Success,
            UpdatedInput = new Dictionary<string, JsonElement> { ["action"] = JsonElementHelper.FromString("Proceed") }
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.PreCompact,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { proceedResult }.ToAsyncEnumerable());

        var context = new CompactHookContext
        {
            SessionId = "session-1",
            Trigger = "auto",
            CurrentTokenCount = 10000,
            TargetTokenCount = 5000
        };

        var result = await _sut.OnPreCompactAsync(context).ConfigureAwait(true);

        result.Action.Should().Be(CompactHookAction.Proceed);
        result.ShouldCompact.Should().BeTrue();
    }

    [Fact]
    public async Task OnPostCompactAsync_NoErrors_ShouldCompleteSuccessfully()
    {
        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.PostCompact,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableEmpty<HookResult>());

        var context = new CompactHookContext
        {
            SessionId = "session-1",
            Trigger = "auto",
            CurrentTokenCount = 5000,
            TargetTokenCount = 5000
        };

        var postCompactData = new PostCompactData
        {
            Compacted = true,
            Level = "moderate",
            Trigger = "auto",
            PreCompactTokenCount = 10000,
            PostCompactTokenCount = 5000,
            MessagesRemoved = 5,
            MessagesPreserved = 10
        };

        var act = async () => await _sut.OnPostCompactAsync(context, postCompactData).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnPostCompactAsync_NonBlockingError_ShouldNotThrow()
    {
        var errorResult = new HookResult
        {
            Outcome = HookOutcome.NonBlockingError,
            Message = "Hook error occurred"
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.PostCompact,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { errorResult }.ToAsyncEnumerable());

        var context = new CompactHookContext
        {
            SessionId = "session-1",
            Trigger = "auto",
            CurrentTokenCount = 5000,
            TargetTokenCount = 5000
        };

        var postCompactData = new PostCompactData
        {
            Compacted = true,
            PreCompactTokenCount = 10000,
            PostCompactTokenCount = 5000,
            MessagesRemoved = 3,
            MessagesPreserved = 8
        };

        var act = async () => await _sut.OnPostCompactAsync(context, postCompactData).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnPreCompactAsync_ShouldPassCorrectPayload()
    {
        Dictionary<string, JsonElement>? capturedPayload = null;

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.PreCompact,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<HookEvent, Dictionary<string, JsonElement>, string?, string?, CancellationToken>(
                (_, payload, _, _, _) => capturedPayload = payload)
            .Returns(AsyncEnumerableEmpty<HookResult>());

        var context = new CompactHookContext
        {
            SessionId = "session-42",
            Trigger = "manual",
            CurrentTokenCount = 15000,
            TargetTokenCount = 8000
        };

        await _sut.OnPreCompactAsync(context).ConfigureAwait(true);

        capturedPayload.Should().NotBeNull();
        capturedPayload!["sessionId"].GetString().Should().Be("session-42");
        capturedPayload["trigger"].GetString().Should().Be("manual");
        capturedPayload["currentTokenCount"].GetInt32().Should().Be(15000);
        capturedPayload["targetTokenCount"].GetInt32().Should().Be(8000);
    }

    private static async IAsyncEnumerable<T> AsyncEnumerableEmpty<T>()
    {
        yield break;
    }
}

file static class TestAsyncEnumerableExtensions
{
    public static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }
}
