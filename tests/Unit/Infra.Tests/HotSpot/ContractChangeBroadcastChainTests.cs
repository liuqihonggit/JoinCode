namespace Infra.Tests.HotSpot;


/// <summary>
/// 断裂点4+5 集成测试 — 队长改热文件 → 广播 → Worker队列收到通知
/// </summary>
public sealed class ContractChangeBroadcastChainTests
{
    private static (FileWriteListenerRegistry registry, ContractChangeNotificationRouter router, IntentCollector collector) Setup(string captainId = "captain")
    {
        var collector = new IntentCollector();
        var detector = new HotFileDetector();
        var tracker = new HotSpotTracker(collector, detector);
        var mailbox = new Mock<IMailbox>();
        var broadcaster = new ContractChangeBroadcaster(detector, mailbox.Object);
        var router = new ContractChangeNotificationRouter();
        var registry = new FileWriteListenerRegistry();

        var intentListener = new IntentReportFileWriteListener(collector, detector, captainId);
        var broadcastListener = new ContractChangeBroadcastListener(broadcaster, detector, tracker, router, captainId);
        registry.Register(intentListener);
        registry.Register(broadcastListener);
        return (registry, router, collector);
    }

    [Fact]
    public void Chain_CaptainWritesHotFile_AfterWorkerClaimed_ShouldEnqueueNotificationToWorker()
    {
        var (registry, router, _) = Setup("captain-1");

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "write", AgentId = "worker-1" });
        var workerQueue = router.GetOrCreateQueue("worker-1");
        workerQueue.Should().BeEmpty();

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "edit", AgentId = "captain-1" });

        workerQueue.Should().NotBeEmpty();
        var notification = string.Empty;
        workerQueue.TryDequeue(out notification).Should().BeTrue();
        notification.Should().Contain("IFoo.cs");
        notification.Should().Contain("git pull");
    }

    [Fact]
    public void Chain_CaptainWritesNonHotFile_ShouldNotBroadcast()
    {
        var (registry, router, _) = Setup("captain-1");

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Utils/Helper.cs", Operation = "write", AgentId = "worker-1" });
        var workerQueue = router.GetOrCreateQueue("worker-1");

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Utils/Helper.cs", Operation = "edit", AgentId = "captain-1" });

        workerQueue.Should().BeEmpty();
    }

    [Fact]
    public void Chain_WorkerWritesHotFile_ShouldNotBroadcast()
    {
        var (registry, router, _) = Setup("captain-1");

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "write", AgentId = "worker-1" });
        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "edit", AgentId = "worker-2" });

        var worker1Queue = router.GetOrCreateQueue("worker-1");
        worker1Queue.Should().BeEmpty();
    }

    [Fact]
    public void Chain_CaptainWritesHotFile_NoDependentWorkers_ShouldNotBroadcast()
    {
        var (registry, router, _) = Setup("captain-1");

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "write", AgentId = "captain-1" });

        var queues = router.GetOrCreateQueue("any-worker");
        queues.Should().BeEmpty();
    }
}
