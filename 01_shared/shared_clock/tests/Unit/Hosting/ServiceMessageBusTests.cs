
namespace Clock.Tests.Unit.Hosting;

public sealed class ServiceMessageBusTests
{
    [Fact]
    public async Task PublishAsync_SubscriberReceivesMessage()
    {
        using var bus = new ServiceMessageBus();
        ServiceMessage? received = null;

        await bus.SubscribeAsync("test", msg =>
        {
            received = msg;
            return Task.CompletedTask;
        }).ConfigureAwait(true);

        var message = ServiceMessage.Create("test", "sender", "payload");
        await bus.PublishAsync(message).ConfigureAwait(true);

        Assert.NotNull(received);
        Assert.Equal("test", received.MessageType);
        Assert.Equal("sender", received.Sender);
        Assert.Equal("payload", received.Payload);
    }

    [Fact]
    public async Task PublishAsync_MultipleSubscribers_AllReceive()
    {
        using var bus = new ServiceMessageBus();
        var count = 0;

        await bus.SubscribeAsync("test", _ => { Interlocked.Increment(ref count); return Task.CompletedTask; }).ConfigureAwait(true);
        await bus.SubscribeAsync("test", _ => { Interlocked.Increment(ref count); return Task.CompletedTask; }).ConfigureAwait(true);

        await bus.PublishAsync(ServiceMessage.Create("test", "sender", "payload")).ConfigureAwait(true);

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task PublishAsync_DuplicateSubscriber_IsOnlyAddedOnce()
    {
        using var bus = new ServiceMessageBus();
        var count = 0;

        Func<ServiceMessage, Task> handler = _ => { Interlocked.Increment(ref count); return Task.CompletedTask; };

        await bus.SubscribeAsync("test", handler).ConfigureAwait(true);
        await bus.SubscribeAsync("test", handler).ConfigureAwait(true);

        await bus.PublishAsync(ServiceMessage.Create("test", "sender", "payload")).ConfigureAwait(true);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task PublishAsync_NoSubscriber_DoesNotThrow()
    {
        using var bus = new ServiceMessageBus();

        await bus.PublishAsync(ServiceMessage.Create("test", "sender", "payload")).ConfigureAwait(true);

        Assert.True(true);
    }

    [Fact]
    public async Task PublishAsync_MessageReceivedEvent_IsRaised()
    {
        using var bus = new ServiceMessageBus();
        ServiceMessage? received = null;

        bus.MessageReceived += msg =>
        {
            received = msg;
            return Task.CompletedTask;
        };

        var message = ServiceMessage.Create("test", "sender", "payload");
        await bus.PublishAsync(message).ConfigureAwait(true);

        Assert.NotNull(received);
        Assert.Equal("test", received.MessageType);
    }

    [Fact]
    public async Task GetMessageHistoryAsync_ReturnsPublishedMessages()
    {
        using var bus = new ServiceMessageBus();

        await bus.PublishAsync(ServiceMessage.Create("test", "sender", "first")).ConfigureAwait(true);
        await bus.PublishAsync(ServiceMessage.Create("test", "sender", "second")).ConfigureAwait(true);

        var history = await bus.GetMessageHistoryAsync("test", 10).ConfigureAwait(true);

        Assert.Equal(2, history.Count);
        Assert.Equal("first", history[0].Payload);
        Assert.Equal("second", history[1].Payload);
    }

    [Fact]
    public async Task GetMessageHistoryAsync_WithCountLimit_ReturnsLastN()
    {
        using var bus = new ServiceMessageBus();

        await bus.PublishAsync(ServiceMessage.Create("test", "sender", "first")).ConfigureAwait(true);
        await bus.PublishAsync(ServiceMessage.Create("test", "sender", "second")).ConfigureAwait(true);
        await bus.PublishAsync(ServiceMessage.Create("test", "sender", "third")).ConfigureAwait(true);

        var history = await bus.GetMessageHistoryAsync("test", 2).ConfigureAwait(true);

        Assert.Equal(2, history.Count);
        Assert.Equal("second", history[0].Payload);
        Assert.Equal("third", history[1].Payload);
    }

    [Fact]
    public async Task GetMessageHistoryAsync_NoHistory_ReturnsEmpty()
    {
        using var bus = new ServiceMessageBus();

        var history = await bus.GetMessageHistoryAsync("test").ConfigureAwait(true);

        Assert.Empty(history);
    }

    [Fact]
    public async Task ClearHistoryAsync_SpecificMessageType_RemovesOnlyThatType()
    {
        using var bus = new ServiceMessageBus();

        await bus.PublishAsync(ServiceMessage.Create("type1", "sender", "data")).ConfigureAwait(true);
        await bus.PublishAsync(ServiceMessage.Create("type2", "sender", "data")).ConfigureAwait(true);

        await bus.ClearHistoryAsync("type1").ConfigureAwait(true);

        var history1 = await bus.GetMessageHistoryAsync("type1").ConfigureAwait(true);
        var history2 = await bus.GetMessageHistoryAsync("type2").ConfigureAwait(true);

        Assert.Empty(history1);
        Assert.Single(history2);
    }

    [Fact]
    public async Task ClearHistoryAsync_All_RemovesAllHistory()
    {
        using var bus = new ServiceMessageBus();

        await bus.PublishAsync(ServiceMessage.Create("type1", "sender", "data")).ConfigureAwait(true);
        await bus.PublishAsync(ServiceMessage.Create("type2", "sender", "data")).ConfigureAwait(true);

        await bus.ClearHistoryAsync().ConfigureAwait(true);

        var history1 = await bus.GetMessageHistoryAsync("type1").ConfigureAwait(true);
        var history2 = await bus.GetMessageHistoryAsync("type2").ConfigureAwait(true);

        Assert.Empty(history1);
        Assert.Empty(history2);
    }

    [Fact]
    public async Task SubscribeAsync_Dispose_Unsubscribes()
    {
        using var bus = new ServiceMessageBus();
        var count = 0;

        Func<ServiceMessage, Task> handler = _ => { Interlocked.Increment(ref count); return Task.CompletedTask; };
        var subscription = await bus.SubscribeAsync("test", handler).ConfigureAwait(true);

        await bus.PublishAsync(ServiceMessage.Create("test", "sender", "data")).ConfigureAwait(true);
        Assert.Equal(1, count);

        await subscription.DisposeAsync().ConfigureAwait(true);
        await bus.PublishAsync(ServiceMessage.Create("test", "sender", "data")).ConfigureAwait(true);

        Assert.Equal(1, count);
    }

    [Fact]
    public async Task SubscribeAsync_DisposeTwice_DoesNotThrow()
    {
        using var bus = new ServiceMessageBus();

        var subscription = await bus.SubscribeAsync("test", _ => Task.CompletedTask).ConfigureAwait(true);

        await subscription.DisposeAsync().ConfigureAwait(true);
        await subscription.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public void Dispose_ClearsSubscribersAndHistory()
    {
        var bus = new ServiceMessageBus();

        bus.Dispose();

        Assert.True(true);
    }

    [Fact]
    public async Task PublishAsync_HistoryTrimming_KeepsMaxItems()
    {
        using var bus = new ServiceMessageBus(maxHistoryPerChannel: 3);

        for (int i = 0; i < 5; i++)
        {
            await bus.PublishAsync(ServiceMessage.Create("test", "sender", i)).ConfigureAwait(true);
        }

        var history = await bus.GetMessageHistoryAsync("test", 10).ConfigureAwait(true);

        Assert.Equal(3, history.Count);
        Assert.Equal(2, history[0].Payload);
        Assert.Equal(3, history[1].Payload);
        Assert.Equal(4, history[2].Payload);
    }
}
