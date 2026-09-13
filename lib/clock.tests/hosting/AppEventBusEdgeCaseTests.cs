
namespace Clock.Tests.Unit.Hosting;

public sealed class AppEventBusEdgeCaseTests
{
    [Fact]
    public async Task PublishAsync_NullEvent_Throws()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);

        await Assert.ThrowsAsync<ArgumentNullException>(() => eventBus.PublishAsync(null!)).ConfigureAwait(true);
    }

    [Fact]
    public async Task SubscribeAsync_NullHandler_Throws()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);

        await Assert.ThrowsAsync<ArgumentNullException>(() => eventBus.SubscribeAsync(ServiceMessageType.TurnStarted, null!)).ConfigureAwait(true);
    }

    [Fact]
    public async Task PublishAsync_NonAppEventPayload_IsIgnored()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        AppEvent? received = null;

        await eventBus.SubscribeAllAsync(e => received = e).ConfigureAwait(true);
        await messageBus.PublishAsync(ServiceMessage.Create("other", "sender", "raw payload")).ConfigureAwait(true);

        Assert.Null(received);
    }

    [Fact]
    public async Task PublishAsync_SubscriberThrows_DoesNotBreakOtherSubscribers()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        var receivedCount = 0;

        await eventBus.SubscribeAsync(ServiceMessageType.TurnStarted, _ => throw new InvalidOperationException("fail")).ConfigureAwait(true);
        await eventBus.SubscribeAsync(ServiceMessageType.TurnStarted, _ => { Interlocked.Increment(ref receivedCount); }).ConfigureAwait(true);

        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.TurnStarted)).ConfigureAwait(true);

        Assert.Equal(1, receivedCount);
    }

    [Fact]
    public async Task SubscribeAllAsync_ReceivesDifferentKinds()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        var received = new List<AppEvent>();

        await eventBus.SubscribeAllAsync(e => received.Add(e)).ConfigureAwait(true);
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.TurnStarted)).ConfigureAwait(true);
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.SessionStarted)).ConfigureAwait(true);
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.GoalAchieved)).ConfigureAwait(true);

        Assert.Equal(3, received.Count);
    }

    [Fact]
    public async Task SubscribeAllAsync_AndSpecificSubscriber_BothReceive()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        var allCount = 0;
        var specificCount = 0;

        await eventBus.SubscribeAllAsync(_ => Interlocked.Increment(ref allCount)).ConfigureAwait(true);
        await eventBus.SubscribeAsync(ServiceMessageType.TurnStarted, _ => Interlocked.Increment(ref specificCount)).ConfigureAwait(true);

        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.TurnStarted)).ConfigureAwait(true);

        Assert.Equal(1, allCount);
        Assert.Equal(1, specificCount);
    }

    [Fact]
    public async Task PublishAsync_DefaultSender_IsAppEventBus()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        AppEvent? received = null;

        await eventBus.SubscribeAsync(ServiceMessageType.TurnStarted, e => received = e).ConfigureAwait(true);
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.TurnStarted)).ConfigureAwait(true);

        Assert.Equal("AppEventBus", received?.Sender);
    }

    [Fact]
    public async Task PublishAsync_CustomSender_IsPreserved()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        AppEvent? received = null;

        await eventBus.SubscribeAsync(ServiceMessageType.TurnStarted, e => received = e).ConfigureAwait(true);
        await eventBus.PublishAsync(AppEvent.Create(ServiceMessageType.TurnStarted, sender: "CustomSender")).ConfigureAwait(true);

        Assert.Equal("CustomSender", received?.Sender);
    }

    [Fact]
    public async Task PublishAsync_Timestamp_IsPreserved()
    {
        var messageBus = new ServiceMessageBus();
        var eventBus = new AppEventBus(messageBus);
        AppEvent? received = null;
        var timestamp = DateTime.UtcNow.AddDays(-1);

        await eventBus.SubscribeAsync(ServiceMessageType.TurnStarted, e => received = e).ConfigureAwait(true);
        await eventBus.PublishAsync(new AppEvent { Kind = ServiceMessageType.TurnStarted, Timestamp = timestamp }).ConfigureAwait(true);

        Assert.Equal(timestamp, received?.Timestamp);
    }
}
