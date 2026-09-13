namespace Clock.Tests.Unit.Hosting;

public sealed class AppEventBusTests
{
    [Fact]
    public async Task PublishAsync_SubscriberReceivesEvent()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        AppEvent? received = null;

        await eventBus.SubscribeAsync(ServiceMessageType.TurnStarted, e => received = e).ConfigureAwait(true);
        var appEvent = AppEvent.Create(ServiceMessageType.TurnStarted, "User asked a question", sender: "CliSession");
        await eventBus.PublishAsync(appEvent).ConfigureAwait(true);

        Assert.NotNull(received);
        Assert.Equal(ServiceMessageType.TurnStarted, received.Kind);
        Assert.Equal("User asked a question", received.Detail);
        Assert.Equal("CliSession", received.Sender);
    }

    [Fact]
    public async Task PublishAsync_MultipleSubscribers_AllReceive()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        var count = 0;

        await eventBus.SubscribeAsync(ServiceMessageType.CompactionStarted, _ => Interlocked.Increment(ref count)).ConfigureAwait(true);
        await eventBus.SubscribeAsync(ServiceMessageType.CompactionStarted, _ => Interlocked.Increment(ref count)).ConfigureAwait(true);

        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.CompactionStarted)).ConfigureAwait(true);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task PublishAsync_UnsubscribedKind_NotReceived()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        AppEvent? received = null;

        await eventBus.SubscribeAsync(ServiceMessageType.TurnStarted, e => received = e).ConfigureAwait(true);
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.TurnCompleted)).ConfigureAwait(true);

        Assert.Null(received);
    }

    [Fact]
    public async Task SubscribeAsync_Unsubscribe_StopsReceiving()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        var count = 0;

        var subscription = await eventBus.SubscribeAsync(ServiceMessageType.GoalAchieved, _ => Interlocked.Increment(ref count)).ConfigureAwait(true);
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.GoalAchieved)).ConfigureAwait(true);
        Assert.Equal(1, count);

        await subscription.DisposeAsync().ConfigureAwait(true);
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.GoalAchieved)).ConfigureAwait(true);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SubscribeAllAsync_ReceivesAllKinds()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        var received = new List<AppEvent>();

        await eventBus.SubscribeAllAsync(e => received.Add(e)).ConfigureAwait(true);
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.TurnStarted)).ConfigureAwait(true);
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.CompactionCompleted)).ConfigureAwait(true);

        Assert.Equal(2, received.Count);
        Assert.Equal(ServiceMessageType.TurnStarted, received[0].Kind);
        Assert.Equal(ServiceMessageType.CompactionCompleted, received[1].Kind);
    }

    [Fact]
    public async Task PublishAsync_WithSessionId_PreservedInEvent()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        AppEvent? received = null;

        await eventBus.SubscribeAsync(ServiceMessageType.SessionStarted, e => received = e).ConfigureAwait(true);
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.SessionStarted, sessionId: "session-123")).ConfigureAwait(true);

        Assert.NotNull(received);
        Assert.Equal("session-123", received.SessionId);
    }

    [Fact]
    public async Task PublishAsync_WithDataPayload_PreservedInEvent()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        AppEvent? received = null;

        await eventBus.SubscribeAsync(ServiceMessageType.ServiceStatusChanged, e => received = e).ConfigureAwait(true);
        var data = new { ServiceName = "ChatService", Status = "Running" };
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.ServiceStatusChanged, data: data)).ConfigureAwait(true);

        Assert.NotNull(received);
        Assert.NotNull(received.Data);
    }
}
