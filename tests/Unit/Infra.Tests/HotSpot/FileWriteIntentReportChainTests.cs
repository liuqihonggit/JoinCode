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

    [Theory]
    [InlineData("edit-regex")]
    [InlineData("insert-lines")]
    [InlineData("delete-lines")]
    [InlineData("batch-edit")]
    [InlineData("apply-patch")]
    public void Chain_AllWriteOperations_ShouldTriggerIntentReport(string operation)
    {
        var (collector, tracker, registry, _) = Setup();
        const string hotFile = "src/Abstractions/IBar.cs";

        registry.Notify(new FileWriteEventArgs { FilePath = hotFile, Operation = operation, AgentId = "worker-1" });

        var intents = collector.GetIntents(hotFile);
        intents.Should().HaveCount(1);
        intents[0].WorkerId.Should().Be("worker-1");
        intents[0].Intent.Should().Be(ModifyIntent.ContractChange);
        tracker.IsHotSpot(hotFile).Should().BeTrue();
    }

    [Fact]
    public void Chain_BatchEditMultipleFiles_ShouldReportAllPaths()
    {
        var (collector, tracker, registry, _) = Setup();

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IA.cs", Operation = "batch-edit", AgentId = "worker-1" });
        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IB.cs", Operation = "batch-edit", AgentId = "worker-1" });

        collector.GetIntents("src/Abstractions/IA.cs").Should().HaveCount(1);
        collector.GetIntents("src/Abstractions/IB.cs").Should().HaveCount(1);
        tracker.IsHotSpot("src/Abstractions/IA.cs").Should().BeTrue();
        tracker.IsHotSpot("src/Abstractions/IB.cs").Should().BeTrue();
    }

    [Fact]
    public void Chain_ApplyPatchMultipleFiles_ShouldReportAllModifiedPaths()
    {
        var (collector, tracker, registry, _) = Setup();

        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IA.cs", Operation = "apply-patch", AgentId = "worker-1" });
        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IB.cs", Operation = "apply-patch", AgentId = "worker-1" });
        registry.Notify(new FileWriteEventArgs { FilePath = "src/Abstractions/IC.cs", Operation = "apply-patch", AgentId = "worker-2" });

        tracker.IsHotSpot("src/Abstractions/IA.cs").Should().BeTrue();
        tracker.IsHotSpot("src/Abstractions/IB.cs").Should().BeTrue();
        tracker.GetHotSpotInfo("src/Abstractions/IC.cs").ContractClaimCount.Should().Be(1);
    }
}
