namespace Composition.Tests.Commands;

/// <summary>
/// /desktop 命令单元测试 — 覆盖服务缺失静默/桌面不可用提示安装引导/
/// 移交成功(会话 ID+连接信息)/移交失败提示
/// <para>IDesktopHandoffService 经 DI 注入</para>
/// </summary>
[Collection("command-output")]
public sealed class DesktopCommandTest {
    private static (DesktopCommand Command, Mock<IDesktopHandoffService> Handoff, ChatCommandContext Context) Arrange(
        bool withService = true,
        bool isAvailable = true,
        bool handoffResult = true,
        string? connectionInfo = "named-pipe://jcc-desktop") {
        var handoff = new Mock<IDesktopHandoffService>();
        handoff.Setup(h => h.IsDesktopAvailable).Returns(isAvailable);
        handoff.Setup(h => h.DesktopConnectionInfo).Returns(connectionInfo);
        handoff.Setup(h => h.HandoffToDesktopAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(handoffResult);

        var services = CommandTestHarness.CreateServices(
            configureDi: di => {
                if (withService) di.AddSingleton(handoff.Object);
            });
        var context = CommandTestHarness.CreateContext(services, sessionId: "sess-desktop");
        return (new DesktopCommand(), handoff, context);
    }

    [Fact]
    public async Task Execute_WithoutService_ReturnsContinueSilently() {
        var (cmd, handoff, context) = Arrange(withService: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().BeEmpty("移交服务缺失应静默降级");
        handoff.Verify(h => h.HandoffToDesktopAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_DesktopUnavailable_ShowsInstallGuide() {
        var (cmd, handoff, context) = Arrange(isAvailable: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未检测到桌面应用。");
        output.Should().Contain("请安装 jcc-desktop 桌面应用");
        output.Should().Contain("/bridge 命令通过命名管道连接");
        handoff.Verify(h => h.HandoffToDesktopAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_HandoffSuccess_ShowsSessionAndConnectionInfo() {
        var (cmd, handoff, context) = Arrange(handoffResult: true);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        handoff.Verify(h => h.HandoffToDesktopAsync("sess-desktop", It.IsAny<CancellationToken>()), Times.Once,
            "移交应携带当前会话 ID");
        output.Should().Contain("会话 sess-desktop 已转移到桌面应用");
        output.Should().Contain("named-pipe://jcc-desktop");
    }

    [Fact]
    public async Task Execute_HandoffFailure_ShowsFailureHint() {
        var (cmd, handoff, context) = Arrange(handoffResult: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("会话转移失败，请检查桌面应用是否正在运行");
        output.Should().NotContain("已转移到桌面应用");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new DesktopCommand();
        cmd.Name.Should().Be("desktop");
        cmd.Usage.Should().Be("/desktop");
        cmd.Aliases.Should().Contain("app");
        cmd.IsHidden.Should().BeTrue();
    }
}
