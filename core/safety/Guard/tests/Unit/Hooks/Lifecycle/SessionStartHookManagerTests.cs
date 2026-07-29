namespace Guard.Tests.Hooks.Lifecycle;

public sealed class SessionStartHookManagerTests
{
    private static readonly string DefaultOpenAiModelId = ModelConfigLoader.GetDefaultModelId("openai");

    private readonly Mock<IHookOrchestrator> _orchestratorMock;
    private readonly SessionStartHookManager _sut;

    public SessionStartHookManagerTests()
    {
        _orchestratorMock = new Mock<IHookOrchestrator>();
        _sut = new SessionStartHookManager(_orchestratorMock.Object, NullLogger<SessionStartHookManager>.Instance);
    }

    [Fact]
    public void Constructor_NullOrchestrator_ShouldThrowArgumentNullException()
    {
        var act = () => new SessionStartHookManager(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task OnSessionStartAsync_NoHooks_ShouldReturnProceedWithEmptyConfig()
    {
        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.SessionStart,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableEmpty<HookResult>());

        var context = new SessionStartHookContext
        {
            SessionId = "session-1",
            Source = "cli"
        };

        var result = await _sut.OnSessionStartAsync(context).ConfigureAwait(true);

        result.ShouldProceed.Should().BeTrue();
        result.AdditionalConfig.Should().BeEmpty();
    }

    [Fact]
    public async Task OnSessionStartAsync_BlockingHook_ShouldReturnNotProceed()
    {
        var blockingResult = new HookResult
        {
            Outcome = HookOutcome.Blocking,
            Message = "Session blocked by policy"
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.SessionStart,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { blockingResult }.ToAsyncEnumerable());

        var context = new SessionStartHookContext
        {
            SessionId = "session-1",
            Source = "api"
        };

        var result = await _sut.OnSessionStartAsync(context).ConfigureAwait(true);

        result.ShouldProceed.Should().BeFalse();
        result.Message.Should().Be("Session blocked by policy");
    }

    [Fact]
    public async Task OnSessionStartAsync_PreventContinuation_ShouldReturnNotProceed()
    {
        var preventResult = new HookResult
        {
            Outcome = HookOutcome.Success,
            PreventContinuation = true,
            Message = "Prevented"
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.SessionStart,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { preventResult }.ToAsyncEnumerable());

        var context = new SessionStartHookContext
        {
            SessionId = "session-1",
            Source = "cli"
        };

        var result = await _sut.OnSessionStartAsync(context).ConfigureAwait(true);

        result.ShouldProceed.Should().BeFalse();
        result.Message.Should().Be("Prevented");
    }

    [Fact]
    public async Task OnSessionStartAsync_UpdatedInput_ShouldMergeIntoAdditionalConfig()
    {
        var hookResult = new HookResult
        {
            Outcome = HookOutcome.Success,
            UpdatedInput = new Dictionary<string, JsonElement>
            {
                ["customKey"] = JsonElementHelper.FromString("customValue"),
                ["numericKey"] = JsonElementHelper.FromInt32(42)
            }
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.SessionStart,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { hookResult }.ToAsyncEnumerable());

        var context = new SessionStartHookContext
        {
            SessionId = "session-1",
            Source = "cli"
        };

        var result = await _sut.OnSessionStartAsync(context).ConfigureAwait(true);

        result.ShouldProceed.Should().BeTrue();
        result.AdditionalConfig["customKey"].GetString().Should().Be("customValue");
        result.AdditionalConfig["numericKey"].GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task OnSessionStartAsync_AdditionalContext_ShouldBeIncludedInConfig()
    {
        var hookResult = new HookResult
        {
            Outcome = HookOutcome.Success,
            AdditionalContext = "Extra context info"
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.SessionStart,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { hookResult }.ToAsyncEnumerable());

        var context = new SessionStartHookContext
        {
            SessionId = "session-1",
            Source = "cli"
        };

        var result = await _sut.OnSessionStartAsync(context).ConfigureAwait(true);

        result.ShouldProceed.Should().BeTrue();
        result.AdditionalConfig["additionalContext"].GetString().Should().Be("Extra context info");
    }

    [Fact]
    public async Task OnSessionStartAsync_InitialUserMessage_ShouldBeIncludedInConfig()
    {
        var hookResult = new HookResult
        {
            Outcome = HookOutcome.Success,
            InitialUserMessage = "Hello, start session"
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.SessionStart,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { hookResult }.ToAsyncEnumerable());

        var context = new SessionStartHookContext
        {
            SessionId = "session-1",
            Source = "cli"
        };

        var result = await _sut.OnSessionStartAsync(context).ConfigureAwait(true);

        result.ShouldProceed.Should().BeTrue();
        result.AdditionalConfig["initialUserMessage"].GetString().Should().Be("Hello, start session");
    }

    [Fact]
    public async Task OnSessionStartAsync_ShouldPassCorrectPayload()
    {
        Dictionary<string, JsonElement>? capturedPayload = null;

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.SessionStart,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<HookEvent, Dictionary<string, JsonElement>, string?, string?, CancellationToken>(
                (_, payload, _, _, _) => capturedPayload = payload)
            .Returns(AsyncEnumerableEmpty<HookResult>());

        var context = new SessionStartHookContext
        {
            SessionId = "session-42",
            Source = "api",
            Configuration = new Dictionary<string, JsonElement> { ["model"] = JsonElementHelper.FromString(DefaultOpenAiModelId) }
        };

        await _sut.OnSessionStartAsync(context).ConfigureAwait(true);

        capturedPayload.Should().NotBeNull();
        capturedPayload!["sessionId"].GetString().Should().Be("session-42");
        capturedPayload["source"].GetString().Should().Be("api");
        capturedPayload["configuration"].ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task OnSessionStartAsync_ShouldUseSourceAsMatcher()
    {
        string? capturedMatcher = null;

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.SessionStart,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<HookEvent, Dictionary<string, JsonElement>, string?, string?, CancellationToken>(
                (_, _, matcher, _, _) => capturedMatcher = matcher)
            .Returns(AsyncEnumerableEmpty<HookResult>());

        var context = new SessionStartHookContext
        {
            SessionId = "session-1",
            Source = "webhook"
        };

        await _sut.OnSessionStartAsync(context).ConfigureAwait(true);

        capturedMatcher.Should().Be("webhook");
    }

    private static async IAsyncEnumerable<T> AsyncEnumerableEmpty<T>()
    {
        yield break;
    }
}

file static class SessionStartTestAsyncEnumerableExtensions
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
