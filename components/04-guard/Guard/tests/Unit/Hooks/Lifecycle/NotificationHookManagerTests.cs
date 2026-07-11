namespace Guard.Tests.Hooks.Lifecycle;

public sealed class NotificationHookManagerTests
{
    private readonly Mock<IHookOrchestrator> _orchestratorMock;
    private readonly NotificationHookManager _sut;

    public NotificationHookManagerTests()
    {
        _orchestratorMock = new Mock<IHookOrchestrator>();
        _sut = new NotificationHookManager(_orchestratorMock.Object, NullLogger<NotificationHookManager>.Instance);
    }

    [Fact]
    public void Constructor_NullOrchestrator_ShouldThrowArgumentNullException()
    {
        var act = () => new NotificationHookManager(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task OnNotificationAsync_NoHooks_ShouldCompleteSuccessfully()
    {
        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Notification,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableEmpty<HookResult>());

        var context = new NotificationHookContext
        {
            SessionId = "session-1",
            NotificationType = "info",
            Message = "Task completed"
        };

        var act = async () => await _sut.OnNotificationAsync(context).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnNotificationAsync_WithNonBlockingError_ShouldNotThrow()
    {
        var errorResult = new HookResult
        {
            Outcome = HookOutcome.NonBlockingError,
            Message = "Notification hook error"
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Notification,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(new[] { errorResult }.ToAsyncEnumerable());

        var context = new NotificationHookContext
        {
            SessionId = "session-1",
            NotificationType = "warning",
            Message = "Disk space low"
        };

        var act = async () => await _sut.OnNotificationAsync(context).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnNotificationAsync_MultipleHooks_ShouldProcessAllResults()
    {
        var results = new List<HookResult>
        {
            new() { Outcome = HookOutcome.Success, Message = "Hook1 processed" },
            new() { Outcome = HookOutcome.Success, Message = "Hook2 processed" },
            new() { Outcome = HookOutcome.NonBlockingError, Message = "Hook3 error" }
        };

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Notification,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns(results.ToAsyncEnumerable());

        var context = new NotificationHookContext
        {
            SessionId = "session-1",
            NotificationType = "error",
            Message = "Something went wrong"
        };

        var act = async () => await _sut.OnNotificationAsync(context).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnNotificationAsync_ShouldPassCorrectPayload()
    {
        Dictionary<string, JsonElement>? capturedPayload = null;

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Notification,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<HookEvent, Dictionary<string, JsonElement>, string?, string?, CancellationToken>(
                (_, payload, _, _, _) => capturedPayload = payload)
            .Returns(AsyncEnumerableEmpty<HookResult>());

        var context = new NotificationHookContext
        {
            SessionId = "session-99",
            NotificationType = "progress",
            Message = "50% done",
            Data = new Dictionary<string, JsonElement> { ["percent"] = JsonElementHelper.FromInt32(50) }
        };

        await _sut.OnNotificationAsync(context).ConfigureAwait(true);

        capturedPayload.Should().NotBeNull();
        capturedPayload!["sessionId"].GetString().Should().Be("session-99");
        capturedPayload["notificationType"].GetString().Should().Be("progress");
        capturedPayload["message"].GetString().Should().Be("50% done");
        capturedPayload["data"].ValueKind.Should().Be(JsonValueKind.Object);
    }

    [Fact]
    public async Task OnNotificationAsync_ShouldUseNotificationTypeAsMatcher()
    {
        string? capturedMatcher = null;

        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Notification,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Callback<HookEvent, Dictionary<string, JsonElement>, string?, string?, CancellationToken>(
                (_, _, matcher, _, _) => capturedMatcher = matcher)
            .Returns(AsyncEnumerableEmpty<HookResult>());

        var context = new NotificationHookContext
        {
            SessionId = "session-1",
            NotificationType = "critical",
            Message = "System failure"
        };

        await _sut.OnNotificationAsync(context).ConfigureAwait(true);

        capturedMatcher.Should().Be("critical");
    }

    [Fact]
    public async Task OnNotificationAsync_CancellationRequested_ShouldRespectCancellationToken()
    {
        _orchestratorMock
            .Setup(o => o.ExecuteHooksAsync(
                HookEvent.Notification,
                It.IsAny<Dictionary<string, JsonElement>>(),
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Throws(new OperationCanceledException());

        var context = new NotificationHookContext
        {
            SessionId = "session-1",
            NotificationType = "info",
            Message = "Test"
        };

        var act = async () => await _sut.OnNotificationAsync(context, new CancellationToken(canceled: true)).ConfigureAwait(true);

        await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(true);
    }

    private static async IAsyncEnumerable<T> AsyncEnumerableEmpty<T>()
    {
        yield break;
    }
}

file static class NotificationTestAsyncEnumerableExtensions
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
