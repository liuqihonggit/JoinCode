namespace Infra.Tests.HotSpot;


public sealed class CaptainDispatchGuardTests
{
    private readonly IntentCollector _collector = new();
    private readonly HotFileDetector _detector = new();
    private readonly ICaptainDispatchGuard _sut;

    public CaptainDispatchGuardTests()
    {
        var tracker = new HotSpotTracker(_collector, _detector);
        _sut = new CaptainDispatchGuard(tracker);
    }

    private static FileModifyIntent MakeIntent(string path, ModifyIntent intent, string workerId) =>
        new() { FilePath = path, Intent = intent, WorkerId = workerId, ReportedAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task Check_NoHotSpot_ShouldDispatchToWorker()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/utils/helper.cs", ModifyIntent.ContractChange, "w1")]);

        var decision = _sut.CheckBeforeDispatch(["src/utils/helper.cs"]);

        decision.ShouldCaptainHandle.Should().BeFalse();
        decision.HotSpotFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task Check_HasHotSpot_ShouldCaptainHandle()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange, "w1")]);

        var decision = _sut.CheckBeforeDispatch(["src/Abstractions/IFoo.cs"]);

        decision.ShouldCaptainHandle.Should().BeTrue();
        decision.HotSpotFiles.Should().Contain("src/Abstractions/IFoo.cs");
        decision.Reason.Should().Contain("IFoo.cs");
    }

    [Fact]
    public async Task Check_MixedFiles_ShouldCaptainHandleIfAnyHotSpot()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange, "w1")]);

        var decision = _sut.CheckBeforeDispatch(["src/utils/helper.cs", "src/Abstractions/IFoo.cs"]);

        decision.ShouldCaptainHandle.Should().BeTrue("有热点文件队长自己揽");
        decision.HotSpotFiles.Should().HaveCount(1);
        decision.HotSpotFiles.Should().Contain("src/Abstractions/IFoo.cs");
    }

    [Fact]
    public void Check_EmptyFileList_ShouldDispatchToWorker()
    {
        var decision = _sut.CheckBeforeDispatch([]);

        decision.ShouldCaptainHandle.Should().BeFalse();
    }

    [Fact]
    public async Task Check_NoHotSpotAtAll_ShouldDispatchToWorker()
    {
        var decision = _sut.CheckBeforeDispatch(["src/utils/a.cs", "src/services/b.cs"]);

        decision.ShouldCaptainHandle.Should().BeFalse();
    }
}
