namespace Core.Tests.Plugins;

public sealed class WeakEventBrokerTests
{
    [Fact]
    public void Subscribe_Dispatch_InvokesHandler()
    {
        var source = new object();
        var target = new HandlerTarget();
        WeakEventBroker<string>.Subscribe(source, target, (t, args) => t.Log(args));
        WeakEventBroker<string>.Dispatch(source, "hello");
        Assert.Equal("hello", target.LastReceived);
    }

    [Fact]
    public void Dispatch_MultipleSubscribers_AllInvoked()
    {
        var source = new object();
        var t1 = new HandlerTarget();
        var t2 = new HandlerTarget();
        WeakEventBroker<string>.Subscribe(source, t1, (t, args) => t.Log(args));
        WeakEventBroker<string>.Subscribe(source, t2, (t, args) => t.Log(args));
        WeakEventBroker<string>.Dispatch(source, "msg");
        Assert.Equal("msg", t1.LastReceived);
        Assert.Equal("msg", t2.LastReceived);
        Assert.Equal(2, WeakEventBroker<string>.GetSubscriberCount(source));
    }

    [Fact]
    public void Dispatch_TargetDead_RemovesHandler()
    {
        var source = new object();
        SetupSubscriber(source, out var weakRef);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        WeakEventBroker<string>.Dispatch(source, "after-gc");
        Assert.Equal(0, WeakEventBroker<string>.GetSubscriberCount(source));
        Assert.False(weakRef.IsAlive);
    }

    private static void SetupSubscriber(object source, out WeakReference weakRef)
    {
        var target = new HandlerTarget();
        weakRef = new WeakReference(target);
        WeakEventBroker<string>.Subscribe(source, target, (t, args) => t.Log(args));
    }

    [Fact]
    public void Dispatch_NoSource_NoOp()
    {
        WeakEventBroker<string>.Dispatch(new object(), "nothing");
    }

    private sealed class HandlerTarget
    {
        public string? LastReceived { get; private set; }
        public void Log(string args) => LastReceived = args;
    }
}
