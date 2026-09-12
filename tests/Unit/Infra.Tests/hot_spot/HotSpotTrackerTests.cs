namespace Infra.Tests.HotSpot;


public sealed class HotSpotTrackerTests
{
    private readonly IntentCollector _collector = new();
    private readonly HotFileDetector _detector = new();
    private readonly IHotSpotTracker _sut;

    public HotSpotTrackerTests()
    {
        _sut = new HotSpotTracker(_collector, _detector);
    }

    private static FileModifyIntent MakeIntent(string path, ModifyIntent intent, string workerId) =>
        new() { FilePath = path, Intent = intent, WorkerId = workerId, ReportedAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task IsHotSpot_HotFileOneContractClaim_ShouldTrigger()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange, "w1")]);

        _sut.IsHotSpot("src/Abstractions/IFoo.cs").Should().BeTrue("热文件1个契约认领即触发（阈值=1）");
    }

    [Fact]
    public async Task IsHotSpot_HotFileCaptainOnly_ShouldNotTrigger()
    {
        await _collector.ReportAsync("captain", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange, "captain")]);

        _sut.IsHotSpot("src/Abstractions/IFoo.cs").Should().BeFalse("队长修改不计入认领集合");
    }

    [Fact]
    public async Task IsHotSpot_HotFileInternalOnly_ShouldNotTrigger()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.InternalChange, "w1")]);

        _sut.IsHotSpot("src/Abstractions/IFoo.cs").Should().BeFalse("内部修改不触发热点");
    }

    [Fact]
    public async Task IsHotSpot_NormalFileOneContractClaim_ShouldNotTrigger()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/utils/helper.cs", ModifyIntent.ContractChange, "w1")]);

        _sut.IsHotSpot("src/utils/helper.cs").Should().BeFalse("非热文件1个契约认领未达阈值3");
    }

    [Fact]
    public async Task IsHotSpot_NormalFileThreeContractClaims_ShouldTrigger()
    {
        var file = "src/utils/helper.cs";
        await _collector.ReportAsync("w1", [MakeIntent(file, ModifyIntent.ContractChange, "w1")]);
        await _collector.ReportAsync("w2", [MakeIntent(file, ModifyIntent.ContractChange, "w2")]);
        await _collector.ReportAsync("w3", [MakeIntent(file, ModifyIntent.ContractChange, "w3")]);

        _sut.IsHotSpot(file).Should().BeTrue("非热文件3个契约认领达阈值触发");
    }

    [Fact]
    public async Task IsHotSpot_NormalFileTwoContractClaims_ShouldNotTrigger()
    {
        var file = "src/utils/helper.cs";
        await _collector.ReportAsync("w1", [MakeIntent(file, ModifyIntent.ContractChange, "w1")]);
        await _collector.ReportAsync("w2", [MakeIntent(file, ModifyIntent.ContractChange, "w2")]);

        _sut.IsHotSpot(file).Should().BeFalse("非热文件2个契约认领未达阈值3");
    }

    [Fact]
    public async Task IsHotSpot_CaptainAndWorkerOnHotFile_OnlyWorkerCounts()
    {
        var file = "src/Abstractions/IFoo.cs";
        await _collector.ReportAsync("captain", [MakeIntent(file, ModifyIntent.ContractChange, "captain")]);
        await _collector.ReportAsync("w1", [MakeIntent(file, ModifyIntent.ContractChange, "w1")]);

        var info = _sut.GetHotSpotInfo(file);
        info.ContractClaimCount.Should().Be(1, "队长不计入，只有w1");
        info.IsHotSpot.Should().BeTrue("热文件1个Worker契约认领即触发");
    }

    [Fact]
    public async Task IsHotSpot_MixedIntent_OnlyContractTriggers()
    {
        var file = "src/utils/helper.cs";
        await _collector.ReportAsync("w1", [MakeIntent(file, ModifyIntent.InternalChange, "w1")]);
        await _collector.ReportAsync("w2", [MakeIntent(file, ModifyIntent.InternalChange, "w2")]);
        await _collector.ReportAsync("w3", [MakeIntent(file, ModifyIntent.InternalChange, "w3")]);

        _sut.IsHotSpot(file).Should().BeFalse("3个内部修改不触发热点");
    }

    [Fact]
    public async Task GetHotSpotInfo_ReturnsCorrectCounts()
    {
        var file = "src/Abstractions/IFoo.cs";
        await _collector.ReportAsync("w1", [MakeIntent(file, ModifyIntent.ContractChange, "w1")]);
        await _collector.ReportAsync("w2", [MakeIntent(file, ModifyIntent.InternalChange, "w2")]);
        await _collector.ReportAsync("captain", [MakeIntent(file, ModifyIntent.ContractChange, "captain")]);

        var info = _sut.GetHotSpotInfo(file);

        info.FilePath.Should().Be(file);
        info.ContractClaimCount.Should().Be(1, "只有w1契约改，队长不计入");
        info.InternalClaimCount.Should().Be(1, "只有w2内部改");
        info.IsHotFile.Should().BeTrue("在abstractions目录");
        info.IsHotSpot.Should().BeTrue("热文件1个契约认领即触发");
        info.ClaimingWorkers.Should().BeEquivalentTo(["w1"]);
    }

    [Fact]
    public async Task GetHotSpotFiles_ReturnsAllTriggeredFiles()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange, "w1")]);
        await _collector.ReportAsync("w2", [MakeIntent("src/Abstractions/IBar.cs", ModifyIntent.ContractChange, "w2")]);

        var hotSpots = _sut.GetHotSpotFiles();

        hotSpots.Should().HaveCount(2);
        hotSpots.Should().Contain("src/Abstractions/IFoo.cs");
        hotSpots.Should().Contain("src/Abstractions/IBar.cs");
    }

    [Fact]
    public async Task GetHotSpotFiles_ExcludesNonHotSpots()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange, "w1")]);
        await _collector.ReportAsync("w1", [MakeIntent("src/utils/helper.cs", ModifyIntent.ContractChange, "w1")]);

        var hotSpots = _sut.GetHotSpotFiles();

        hotSpots.Should().HaveCount(1, "只有热文件IFoo触发，helper未达阈值");
        hotSpots.Should().Contain("src/Abstractions/IFoo.cs");
    }

    [Fact]
    public async Task SetThresholds_CustomThreshold_TakesEffect()
    {
        _sut.SetThresholds(hotFileThreshold: 2, normalFileThreshold: 5);

        await _collector.ReportAsync("w1", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange, "w1")]);

        _sut.IsHotSpot("src/Abstractions/IFoo.cs").Should().BeFalse("热文件阈值改为2，1个认领不触发");
    }

    [Fact]
    public void SetThresholds_InvalidValue_ShouldThrow()
    {
        var act = () => _sut.SetThresholds(0, 3);
        act.Should().Throw<ArgumentOutOfRangeException>("阈值必须>=1");
    }

    [Fact]
    public async Task Clear_RemovesAllIntents()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange, "w1")]);
        _sut.IsHotSpot("src/Abstractions/IFoo.cs").Should().BeTrue();

        _sut.Clear();

        _sut.IsHotSpot("src/Abstractions/IFoo.cs").Should().BeFalse("清空后不再有热点");
        _sut.GetHotSpotFiles().Should().BeEmpty();
    }

    [Fact]
    public async Task IsHotSpot_DuplicateWorkerClaims_CountedOnce()
    {
        var file = "src/Abstractions/IFoo.cs";
        await _collector.ReportAsync("w1", [
            MakeIntent(file, ModifyIntent.ContractChange, "w1"),
            MakeIntent(file, ModifyIntent.ContractChange, "w1")
        ]);

        var info = _sut.GetHotSpotInfo(file);
        info.ContractClaimCount.Should().Be(1, "同一Worker多次上报去重计数");
    }
}
