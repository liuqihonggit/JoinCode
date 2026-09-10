namespace Core.Tests.Plugins;

public sealed class PluginContextWeakSubscribeTests
{
    private sealed class EventSource
    {
        public int Value { get; set; }
    }

    private sealed class Subscriber
    {
        public int LastReceived { get; set; }
    }

    [Fact]
    public void WeakSubscribe_DispatchReachesSubscriber()
    {
        var services = new ServiceCollection();
        var ctx = new PluginContext("test", services);
        var source = new EventSource();
        var sub = new Subscriber();

        ctx.WeakSubscribe(source, sub, (Subscriber s, int arg) => s.LastReceived = arg);

        WeakEventBroker<int>.Dispatch(source, 42);
        Assert.Equal(42, sub.LastReceived);
    }

    [Fact]
    public async Task WeakSubscribe_SubscriberCollected_AutoUnsubscribed()
    {
        var services = new ServiceCollection();
        var ctx = new PluginContext("test", services);
        var source = new EventSource();

        WeakReference<Subscriber> weakRef = CreateAndSubscribe(ctx, source);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weakRef.TryGetTarget(out _),
            "订阅者应被 GC 回收");

        WeakEventBroker<int>.Dispatch(source, 99);

        await Task.CompletedTask;
    }

    private static WeakReference<Subscriber> CreateAndSubscribe(PluginContext ctx, EventSource source)
    {
        var sub = new Subscriber();
        ctx.WeakSubscribe(source, sub, (Subscriber s, int arg) => s.LastReceived = arg);
        return new WeakReference<Subscriber>(sub);
    }

    [Fact]
    public void WeakSubscribe_NullSource_Throws()
    {
        var services = new ServiceCollection();
        var ctx = new PluginContext("test", services);
        var sub = new Subscriber();

        Assert.Throws<ArgumentNullException>(() =>
            ctx.WeakSubscribe<Subscriber, int>(null!, sub, (s, arg) => { }));
    }

    [Fact]
    public void WeakSubscribe_NullTarget_Throws()
    {
        var services = new ServiceCollection();
        var ctx = new PluginContext("test", services);
        var source = new EventSource();

        Assert.Throws<ArgumentNullException>(() =>
            ctx.WeakSubscribe(source, (Subscriber)null!, (Subscriber s, int arg) => { }));
    }

    [Fact]
    public void WeakSubscribe_NullHandler_Throws()
    {
        var services = new ServiceCollection();
        var ctx = new PluginContext("test", services);
        var source = new EventSource();
        var sub = new Subscriber();

        Assert.Throws<ArgumentNullException>(() =>
            ctx.WeakSubscribe<Subscriber, int>(source, sub, null!));
    }

    [Fact]
    public void WeakSubscribe_RegisteredInUndoChain()
    {
        var services = new ServiceCollection();
        var ctx = new PluginContext("test", services);
        var source = new EventSource();
        var sub = new Subscriber();

        ctx.WeakSubscribe(source, sub, (Subscriber s, int arg) => s.LastReceived = arg);

        Assert.Single(ctx.GetUndoChain());
    }
}
