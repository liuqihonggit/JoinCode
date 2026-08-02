namespace Guard.Tests.Hooks.Lifecycle;

public sealed class StopHookManagerTests
{
    private readonly Mock<IHookOrchestrator> _orchestratorMock;
    private readonly StopHookManager _sut;

    public StopHookManagerTests()
    {
        _orchestratorMock = new Mock<IHookOrchestrator>();
        _sut = new StopHookManager(_orchestratorMock.Object, NullLogger<StopHookManager>.Instance);
    }

    [Fact]
    public void Constructor_NullOrchestrator_ShouldThrowArgumentNullException()
    {
        var act = () => new StopHookManager(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task OnStopAsync_NoHooks_ShouldReturnDefaultStopResult()
    {
        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Stop,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableEmpty<HookResult>());

        var context = new StopHookContext
        {
            SessionId = "session-1",
            Reason = "user_request"
        };

        var result = await _sut.OnStopAsync(context).ConfigureAwait(true);

        result.ShouldStop.Should().BeTrue();
        result.AdditionalData.Should().BeEmpty();
    }

    [Fact]
    public async Task OnStopAsync_BlockingHook_ShouldReturnContinue()
    {
        var blockingResult = new HookResult
        {
            Outcome = HookOutcome.Blocking,
            Message = "Stop prevented by hook"
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Stop,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { blockingResult }.ToAsyncEnumerable());

        var context = new StopHookContext
        {
            SessionId = "session-1",
            Reason = "timeout"
        };

        var result = await _sut.OnStopAsync(context).ConfigureAwait(true);

        result.ShouldStop.Should().BeFalse();
        result.Message.Should().Be("Stop prevented by hook");
    }

    [Fact]
    public async Task OnStopAsync_PreventContinuation_ShouldReturnContinue()
    {
        var preventResult = new HookResult
        {
            Outcome = HookOutcome.Success,
            PreventContinuation = true,
            Message = "Cannot stop now"
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Stop,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { preventResult }.ToAsyncEnumerable());

        var context = new StopHookContext
        {
            SessionId = "session-1",
            Reason = "user_request"
        };

        var result = await _sut.OnStopAsync(context).ConfigureAwait(true);

        result.ShouldStop.Should().BeFalse();
        result.Message.Should().Be("Cannot stop now");
    }

    [Fact]
    public async Task OnStopAsync_UpdatedInput_ShouldMergeIntoAdditionalData()
    {
        var hookResult = new HookResult
        {
            Outcome = HookOutcome.Success,
            UpdatedInput = new Dictionary<string, JsonElement>
            {
                ["cleanupRequired"] = JsonElementHelper.FromBoolean(true),
                ["pendingTasks"] = JsonElementHelper.FromInt32(3)
            }
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Stop,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { hookResult }.ToAsyncEnumerable());

        var context = new StopHookContext
        {
            SessionId = "session-1",
            Reason = "user_request"
        };

        var result = await _sut.OnStopAsync(context).ConfigureAwait(true);

        result.ShouldStop.Should().BeTrue();
        result.AdditionalData["cleanupRequired"].GetBoolean().Should().BeTrue();
        result.AdditionalData["pendingTasks"].GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task OnStopAsync_MultipleHooks_ShouldMergeAllUpdatedInputs()
    {
        var results = new List<HookResult>
        {
            new()
            {
                Outcome = HookOutcome.Success,
                UpdatedInput = new Dictionary<string, JsonElement> { ["key1"] = JsonElementHelper.FromString("value1") }
            },
            new()
            {
                Outcome = HookOutcome.Success,
                UpdatedInput = new Dictionary<string, JsonElement> { ["key2"] = JsonElementHelper.FromString("value2") }
            }
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Stop,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(results.ToAsyncEnumerable());

        var context = new StopHookContext
        {
            SessionId = "session-1",
            Reason = "user_request"
        };

        var result = await _sut.OnStopAsync(context).ConfigureAwait(true);

        result.ShouldStop.Should().BeTrue();
        result.AdditionalData["key1"].GetString().Should().Be("value1");
        result.AdditionalData["key2"].GetString().Should().Be("value2");
    }

    [Fact]
    public async Task OnStopAsync_ShouldPassCorrectPayload()
    {
        Dictionary<string, JsonElement>? capturedPayload = null;

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Stop,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<HookEvent, Dictionary<string, JsonElement>, string?, string?, CancellationToken>(
                (_, payload, _, _, _) => capturedPayload = payload)
            .Returns(AsyncEnumerableEmpty<HookResult>());

        var context = new StopHookContext
        {
            SessionId = "session-42",
            Reason = "timeout",
            Metadata = new Dictionary<string, JsonElement> { ["elapsed"] = JsonElementHelper.FromInt32(300) }
        };

        await _sut.OnStopAsync(context).ConfigureAwait(true);

        capturedPayload.Should().NotBeNull();
        capturedPayload!["sessionId"].GetString().Should().Be("session-42");
        capturedPayload["reason"].GetString().Should().Be("timeout");
        capturedPayload["metadata"].ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public void StopHookResult_Continue_ShouldReturnShouldStopFalse()
    {
        var result = StopHookResult.Continue("Continue message");

        result.ShouldStop.Should().BeFalse();
        result.Message.Should().Be("Continue message");
    }

    [Fact]
    public void StopHookResult_Stop_ShouldReturnShouldStopTrue()
    {
        var result = StopHookResult.Stop("Stop message");

        result.ShouldStop.Should().BeTrue();
        result.Message.Should().Be("Stop message");
    }

    private static async IAsyncEnumerable<T> AsyncEnumerableEmpty<T>()
    {
        yield break;
    }
}

file static class StopTestAsyncEnumerableExtensions
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
