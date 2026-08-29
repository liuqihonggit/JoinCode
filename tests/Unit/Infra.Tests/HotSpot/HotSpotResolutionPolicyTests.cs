namespace Infra.Tests.HotSpot;


public sealed class HotSpotResolutionPolicyTests
{
    private readonly IntentCollector _collector = new();
    private readonly HotFileDetector _detector = new();
    private readonly IHotSpotResolutionPolicy _sut;

    public HotSpotResolutionPolicyTests()
    {
        var tracker = new HotSpotTracker(_collector, _detector);
        _sut = new HotSpotResolutionPolicy(tracker);
    }

    private static FileModifyIntent MakeIntent(string path, ModifyIntent intent, string workerId) =>
        new() { FilePath = path, Intent = intent, WorkerId = workerId, ReportedAt = DateTimeOffset.UtcNow };

    [Fact]
    public async Task Resolve_NonHotSpot_ShouldReturnNoAction()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/utils/helper.cs", ModifyIntent.ContractChange, "w1")]);

        var resolution = _sut.Resolve("src/utils/helper.cs");

        resolution.RequiresAction.Should().BeFalse();
        resolution.ShouldCaptainTakeOver.Should().BeFalse();
        resolution.WorkersToNotify.Should().BeEmpty();
        resolution.NotificationMessage.Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_HotSpot_ShouldCaptainTakeOverAndNotifyWorkers()
    {
        var file = "src/Abstractions/IFoo.cs";
        await _collector.ReportAsync("w1", [MakeIntent(file, ModifyIntent.ContractChange, "w1")]);

        var resolution = _sut.Resolve(file);

        resolution.ShouldCaptainTakeOver.Should().BeTrue("热点文件队长接管");
        resolution.WorkersToNotify.Should().BeEquivalentTo(["w1"]);
        resolution.RequiresAction.Should().BeTrue();
    }

    [Fact]
    public async Task Resolve_HotSpotMultipleWorkers_ShouldNotifyAllClaimers()
    {
        var file = "src/utils/helper.cs";
        await _collector.ReportAsync("w1", [MakeIntent(file, ModifyIntent.ContractChange, "w1")]);
        await _collector.ReportAsync("w2", [MakeIntent(file, ModifyIntent.ContractChange, "w2")]);
        await _collector.ReportAsync("w3", [MakeIntent(file, ModifyIntent.ContractChange, "w3")]);

        var resolution = _sut.Resolve(file);

        resolution.ShouldCaptainTakeOver.Should().BeTrue();
        resolution.WorkersToNotify.Should().BeEquivalentTo(["w1", "w2", "w3"]);
    }

    [Fact]
    public async Task Resolve_NotificationMessage_ShouldContainFilePath()
    {
        var file = "src/Abstractions/IFoo.cs";
        await _collector.ReportAsync("w1", [MakeIntent(file, ModifyIntent.ContractChange, "w1")]);

        var resolution = _sut.Resolve(file);

        resolution.NotificationMessage.Should().Contain(file);
        resolution.NotificationMessage.Should().Contain("队长接管");
    }

    [Fact]
    public async Task ResolveAll_MultipleHotSpots_ShouldReturnAllResolutions()
    {
        await _collector.ReportAsync("w1", [MakeIntent("src/Abstractions/IFoo.cs", ModifyIntent.ContractChange, "w1")]);
        await _collector.ReportAsync("w2", [MakeIntent("src/Abstractions/IBar.cs", ModifyIntent.ContractChange, "w2")]);

        var resolutions = _sut.ResolveAll();

        resolutions.Should().HaveCount(2);
        resolutions.All(r => r.ShouldCaptainTakeOver).Should().BeTrue();
    }

    [Fact]
    public async Task ResolveAll_NoHotSpots_ShouldReturnEmpty()
    {
        _sut.ResolveAll().Should().BeEmpty();
    }

    [Fact]
    public async Task Resolve_CaptainOnlyClaim_ShouldNotTriggerAction()
    {
        var file = "src/Abstractions/IFoo.cs";
        await _collector.ReportAsync("captain", [MakeIntent(file, ModifyIntent.ContractChange, "captain")]);

        var resolution = _sut.Resolve(file);

        resolution.RequiresAction.Should().BeFalse("队长自己的修改不触发处置");
    }
}
