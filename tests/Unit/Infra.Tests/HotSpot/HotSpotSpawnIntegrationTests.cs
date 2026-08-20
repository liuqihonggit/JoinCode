namespace Infra.Tests.HotSpot;

using Infrastructure.HotSpot;
using Infrastructure.IO.Services.FileOps;

/// <summary>
/// HotSpotSpawnIntegration 单元测试 — Worker spawn 时注册监听器 + 获取通知队列
/// </summary>
public sealed class HotSpotSpawnIntegrationTests
{
    private static HotSpotSpawnIntegration CreateSut(out FileWriteListenerRegistry registry, out ContractChangeNotificationRouter router)
    {
        var collector = new IntentCollector();
        var detector = new HotFileDetector();
        var tracker = new HotSpotTracker(collector, detector);
        var mailbox = new Mock<IMailbox>();
        var broadcaster = new ContractChangeBroadcaster(detector, mailbox.Object);
        router = new ContractChangeNotificationRouter();
        registry = new FileWriteListenerRegistry();
        return new HotSpotSpawnIntegration(registry, collector, detector, broadcaster, tracker, router);
    }

    [Fact]
    public void EnsureListenersRegistered_FirstCall_ShouldRegisterTwoListeners()
    {
        var sut = CreateSut(out var registry, out _);

        sut.EnsureListenersRegistered("captain-1");

        registry.ListenerCount.Should().Be(2);
    }

    [Fact]
    public void EnsureListenersRegistered_SameCaptainId_CalledTwice_ShouldBeIdempotent()
    {
        var sut = CreateSut(out var registry, out _);

        sut.EnsureListenersRegistered("captain-1");
        sut.EnsureListenersRegistered("captain-1");

        registry.ListenerCount.Should().Be(2);
    }

    [Fact]
    public void EnsureListenersRegistered_DifferentCaptainId_ShouldRegisterAgain()
    {
        var sut = CreateSut(out var registry, out _);

        sut.EnsureListenersRegistered("captain-1");
        sut.EnsureListenersRegistered("captain-2");

        registry.ListenerCount.Should().Be(4);
    }

    [Fact]
    public void GetOrCreateNotificationQueue_SameAgentId_ShouldReturnSameQueue()
    {
        var sut = CreateSut(out _, out _);

        var q1 = sut.GetOrCreateNotificationQueue("worker-1");
        var q2 = sut.GetOrCreateNotificationQueue("worker-1");

        q2.Should().BeSameAs(q1);
    }

    [Fact]
    public void GetOrCreateNotificationQueue_DifferentAgentId_ShouldReturnDifferentQueue()
    {
        var sut = CreateSut(out _, out _);

        var q1 = sut.GetOrCreateNotificationQueue("worker-1");
        var q2 = sut.GetOrCreateNotificationQueue("worker-2");

        q2.Should().NotBeSameAs(q1);
    }

    [Fact]
    public void Integration_ListenerRegistered_WorkerWriteHotFile_ShouldTriggerIntentReport()
    {
        var sut = CreateSut(out var registry, out var router);
        sut.EnsureListenersRegistered("captain-1");

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "write", AgentId = "worker-1" });

        var queue = router.GetOrCreateQueue("worker-1");
        queue.Should().BeEmpty();
    }

    [Fact]
    public void Integration_FullChain_CaptainWriteHotFile_AfterWorkerClaim_ShouldEnqueueNotification()
    {
        var sut = CreateSut(out var registry, out var router);
        sut.EnsureListenersRegistered("captain-1");

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "write", AgentId = "worker-1" });
        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "edit", AgentId = "captain-1" });

        var queue = sut.GetOrCreateNotificationQueue("worker-1");
        queue.Should().NotBeEmpty();
        var notification = string.Empty;
        queue.TryDequeue(out notification).Should().BeTrue();
        notification.Should().Contain("git pull");
    }
}
