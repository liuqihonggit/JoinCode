namespace Infra.Tests.HotSpot;

using Infrastructure.HotSpot;
using Infrastructure.IO.Services.FileOps;

/// <summary>
/// 断裂点1+2+3 集成测试 — 验证 Worker 改文件 → IntentCollector 记录 → HotSpotTracker 查询链路
/// </summary>
public sealed class FileWriteIntentReportChainTests
{
    private static (IntentCollector collector, HotSpotTracker tracker, FileWriteListenerRegistry registry, IntentReportFileWriteListener listener) Setup(string captainId = "captain")
    {
        var collector = new IntentCollector();
        var detector = new HotFileDetector();
        var tracker = new HotSpotTracker(collector, detector);
        var registry = new FileWriteListenerRegistry();
        var listener = new IntentReportFileWriteListener(collector, detector, captainId);
        registry.Register(listener);
        return (collector, tracker, registry, listener);
    }

    [Fact]
    public void Chain_WorkerWritesHotFile_ShouldRecordContractChangeAndTriggerHotSpot()
    {
        var (collector, tracker, registry, _) = Setup();

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "write", AgentId = "worker-1" });

        var intents = collector.GetIntents("src/Abstractions/IFoo.cs");
        intents.Should().HaveCount(1);
        intents[0].Intent.Should().Be(ModifyIntent.ContractChange);
        intents[0].WorkerId.Should().Be("worker-1");
        tracker.IsHotSpot("src/Abstractions/IFoo.cs").Should().BeTrue();
    }

    [Fact]
    public void Chain_WorkerWritesNormalFile_ShouldRecordInternalChangeNoHotSpot()
    {
        var (collector, tracker, registry, _) = Setup();

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Utils/Helper.cs", Operation = "write", AgentId = "worker-1" });

        var intents = collector.GetIntents("src/Utils/Helper.cs");
        intents.Should().HaveCount(1);
        intents[0].Intent.Should().Be(ModifyIntent.InternalChange);
        tracker.IsHotSpot("src/Utils/Helper.cs").Should().BeFalse();
    }

    [Fact]
    public void Chain_CaptainWritesHotFile_ShouldNotTriggerHotSpot()
    {
        var (collector, tracker, registry, _) = Setup("captain-1");

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "write", AgentId = "captain-1" });

        var intents = collector.GetIntents("src/Abstractions/IFoo.cs");
        intents.Should().HaveCount(1);
        intents[0].IsFromCaptain.Should().BeTrue();
        tracker.IsHotSpot("src/Abstractions/IFoo.cs").Should().BeFalse();
    }

    [Fact]
    public void Chain_MultipleWorkersWriteSameHotFile_ShouldTriggerHotSpot()
    {
        var (collector, tracker, registry, _) = Setup();

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "write", AgentId = "worker-1" });
        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IFoo.cs", Operation = "edit", AgentId = "worker-2" });

        tracker.IsHotSpot("src/Abstractions/IFoo.cs").Should().BeTrue();
        var info = tracker.GetHotSpotInfo("src/Abstractions/IFoo.cs");
        info.ContractClaimCount.Should().Be(2);
    }

    [Fact]
    public void Chain_FileInBinDirectory_ShouldNotBeHotFile()
    {
        var (collector, tracker, registry, _) = Setup();

        registry.Notify(new FileWriteEventArgs { FilePath = "src/bin/IFoo.cs", Operation = "write", AgentId = "worker-1" });

        var intents = collector.GetIntents("src/bin/IFoo.cs");
        intents[0].Intent.Should().Be(ModifyIntent.InternalChange);
        tracker.IsHotSpot("src/bin/IFoo.cs").Should().BeFalse();
    }
}
