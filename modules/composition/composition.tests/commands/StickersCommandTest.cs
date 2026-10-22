namespace Composition.Tests.Commands;

/// <summary>
/// /stickers 命令单元测试 — 覆盖服务缺失静默/打开成功提示/打开失败回退手动 URL
/// <para>IStickerService 经 DI 注入</para>
/// </summary>
[Collection("command-output")]
public sealed class StickersCommandTest {
    private static (StickersCommand Command, Mock<IStickerService> Sticker, ChatCommandContext Context) Arrange(
        bool withService = true,
        bool openResult = true,
        string pageUrl = "https://stickers.example.com") {
        var sticker = new Mock<IStickerService>();
        sticker.Setup(s => s.OpenStickerPageAsync(It.IsAny<CancellationToken>())).ReturnsAsync(openResult);
        sticker.Setup(s => s.GetStickerPageUrl()).Returns(pageUrl);

        var services = CommandTestHarness.CreateServices(
            configureDi: di => {
                if (withService) di.AddSingleton(sticker.Object);
            });
        var context = CommandTestHarness.CreateContext(services);
        return (new StickersCommand(), sticker, context);
    }

    [Fact]
    public async Task Execute_WithoutService_ReturnsContinueSilently() {
        var (cmd, sticker, context) = Arrange(withService: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().BeEmpty("贴纸服务缺失应静默降级");
        sticker.Verify(s => s.OpenStickerPageAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_OpenSuccess_ShowsBrowserOpeningHint() {
        var (cmd, sticker, context) = Arrange(openResult: true);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        sticker.Verify(s => s.OpenStickerPageAsync(It.IsAny<CancellationToken>()), Times.Once);
        output.Should().Contain("正在浏览器中打开贴纸页面...");
        output.Should().NotContain("手动访问");
    }

    [Fact]
    public async Task Execute_OpenFailure_ShowsManualUrlFallback() {
        var (cmd, sticker, context) = Arrange(openResult: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("打开浏览器失败，请手动访问: https://stickers.example.com");
        sticker.Verify(s => s.GetStickerPageUrl(), Times.Once, "失败分支应回退获取手动访问 URL");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new StickersCommand();
        cmd.Name.Should().Be("stickers");
        cmd.Usage.Should().Be("/stickers");
    }
}
