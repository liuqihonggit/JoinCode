namespace Composition.Tests.Commands;

/// <summary>
/// /buddy 命令单元测试 — 覆盖主路径渲染(ASCII/名称/稀有度/眼睛帽子)/闪光与空帽子形态/
/// 服务缺失静默降级/元数据一致性
/// <para>BuddyService 用 Moq 桩经 DI 注入(GetService 解析)</para>
/// </summary>
[Collection("command-output")]
public sealed class BuddyCommandTest {
    private static BuddyInfo Buddy(
        bool shiny = false,
        string? hat = "wizard") => new() {
        Name = "Pikachu",
        Species = "electric-mouse",
        Rarity = BuddyRarity.Rare,
        Eye = "happy",
        Hat = hat ?? string.Empty,
        Shiny = shiny,
        AsciiArt = @"  /\_/\
 ( o.o )",
    };

    private static (BuddyCommand Command, ChatCommandContext Context, Mock<IBuddyService> Service) Arrange(
        Mock<IBuddyService>? service = null,
        string sessionId = "test-session") {
        var buddyService = service ?? new Mock<IBuddyService>();
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(buddyService.Object));
        var context = CommandTestHarness.CreateContext(services, sessionId: sessionId);
        return (new BuddyCommand(), context, buddyService);
    }

    [Fact]
    public async Task Execute_WithBuddy_RendersAsciiArtAndDetails() {
        var service = new Mock<IBuddyService>();
        service.Setup(s => s.GetBuddy("test-session")).Returns(Buddy());
        var (cmd, context, _) = Arrange(service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain(@"  /\_/\", "ASCII 艺术图应原样渲染");
        output.Should().Contain("  Pikachu (electric-mouse)", "名称与物种应成对渲染");
        output.Should().Contain("  稀有度: Rare");
        output.Should().Contain("  眼睛: happy  帽子: wizard", "眼睛与帽子应在同一行渲染");
        service.Verify(s => s.GetBuddy("test-session"), Times.Once, "应按会话 ID 查询伙伴");
    }

    [Fact]
    public async Task Execute_ShinyBuddy_MarksShinyBadge() {
        var service = new Mock<IBuddyService>();
        service.Setup(s => s.GetBuddy(It.IsAny<string>())).Returns(Buddy(shiny: true));
        var (cmd, context, _) = Arrange(service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("稀有度: Rare ✨闪亮!", "闪光形态应在稀有度后追加徽记");
    }

    [Fact]
    public async Task Execute_EmptyHat_ShowsNoneLabel() {
        var service = new Mock<IBuddyService>();
        service.Setup(s => s.GetBuddy(It.IsAny<string>())).Returns(Buddy(hat: null));
        var (cmd, context, _) = Arrange(service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("帽子: 无", "空帽子应显示占位'无'而非空白");
    }

    [Fact]
    public async Task Execute_WithoutService_ReturnsSilently() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new BuddyCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("服务缺失应静默返回继续会话而非崩溃");
        output.Should().NotContain("Pikachu", "无服务时不应渲染任何伙伴信息");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new BuddyCommand();
        cmd.Name.Should().Be("buddy");
        cmd.Usage.Should().Be("/buddy");
        cmd.Description.Should().Contain("伙伴精灵");
        cmd.IsHidden.Should().BeTrue("buddy 为隐藏命令");
    }
}
