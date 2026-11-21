// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
namespace Infra.Tests.HotSpot;


public sealed class HotFileWatchdogTests
{
    private readonly IntentCollector _collector = new();
    private readonly HotFileDetector _detector = new();
    private readonly IHotFileWatchdog _sut;

    public HotFileWatchdogTests()
    {
        _sut = new HotFileWatchdog(_detector, _collector);
    }

    private static FileModifyIntent MakeIntent(string path, ModifyIntent intent, string workerId) =>
        new() { FilePath = path, Intent = intent, WorkerId = workerId, ReportedAt = DateTimeOffset.UtcNow };

    [Fact]
    public void CheckChange_NormalFile_NoAlert()
    {
        _sut.TryGetChange("src/utils/helper.cs", "w1", out var alert).Should().BeFalse("非热文件不告警");
        alert.Should().BeNull();
    }

    [SuppressMessage("AotSafety", "JCC11003", Justification = "边界可空抑制: 反射/延迟初始化/解析保证非空")]
    [Fact]
    public async Task CheckChange_HotFileNotReported_ShouldAlert()
    {
        _sut.TryGetChange("src/Abstractions/IFoo.cs", "w1", out var alert).Should().BeTrue("热文件被改未上报应告警");

        alert.Should().NotBeNull("热文件被改未上报应告警");
        alert!.FilePath.Should().Be("src/Abstractions/IFoo.cs");
        alert.ChangerId.Should().Be("w1");
        alert.AlertMessage.Should().Contain("IFoo.cs");
        alert.AlertMessage.Should().Contain("w1");
    }

    [Fact]
    public async Task CheckChange_HotFileReported_NoAlert()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange, "w1")]);

        _sut.TryGetChange("src/Abstractions/IFoo.cs", "w1", out var alert).Should().BeFalse("已上报意图不告警");
        alert.Should().BeNull();
    }

    [Fact]
    public async Task CheckChange_CaptainModifiesHotFile_NoAlert()
    {
        _sut.TryGetChange("src/Abstractions/IFoo.cs", "captain", out var alert).Should().BeFalse("队长改热文件不告警");
        alert.Should().BeNull();
    }

    [Fact]
    public async Task CheckChange_HotFileReportedByOtherWorker_StillNoAlert()
    {
        await _collector.ReportAsync("w2", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange, "w2")]);

        _sut.TryGetChange("src/Abstractions/IFoo.cs", "w1", out var alert).Should().BeFalse("已有上报记录（不论谁报的）不告警");
        alert.Should().BeNull();
    }

    [Fact]
    public void CheckChanges_MixedChanges_ShouldAlertOnlyUnreportedHotFiles()
    {
        var changes = new List<HotFileChange>
        {
            new("src/Abstractions/IFoo.cs", "w1"),
            new("src/utils/helper.cs", "w1"),
            new("src/Abstractions/IBar.cs", "w2")
        };

        var alerts = _sut.CheckChanges(changes);

        alerts.Should().HaveCount(2, "2个热文件未上报，1个非热文件不告警");
        alerts.Select(a => a.FilePath).Should().BeEquivalentTo(["src/Abstractions/IFoo.cs", "src/Abstractions/IBar.cs"]);
    }

    [Fact]
    public void CheckChanges_AllNormal_NoAlerts()
    {
        var changes = new List<HotFileChange>
        {
            new("src/utils/a.cs", "w1"),
            new("src/services/b.cs", "w2")
        };

        _sut.CheckChanges(changes).Should().BeEmpty();
    }

    [Fact]
    public void CheckChange_EmptyPath_ShouldThrow()
    {
        var act = () => _sut.TryGetChange("", "w1", out _);
        act.Should().Throw<ArgumentException>();
    }
}
