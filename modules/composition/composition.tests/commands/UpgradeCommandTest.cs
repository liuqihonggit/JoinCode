namespace Composition.Tests.Commands;

/// <summary>
/// /upgrade 命令单元测试（用户口径 update 域）— 覆盖服务缺失降级(回退程序集版本+手动引导)/
/// check(新版本可用/已是最新/获取失败)/force(可用/最新)/download(无更新源/发现+下载成功/下载失败)/
/// apply(安装成功/安装失败)/auto(无更新短路)/未知参数用法
/// <para>IUpgradeService 经 DI 容器解析,用 Moq 注入,不触发真实网络请求</para>
/// </summary>
[Collection("command-output")]
public sealed class UpgradeCommandTest {
    private static (UpgradeCommand Command, Mock<IUpgradeService> Upgrade, ChatCommandContext Context) Arrange(
        string arguments = "",
        Mock<IUpgradeService>? upgradeOverride = null) {
        var upgrade = upgradeOverride ?? new Mock<IUpgradeService>();
        upgrade.Setup(u => u.GetCurrentVersion()).Returns(new Version(1, 0, 0));
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(upgrade.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new UpgradeCommand(), upgrade, context);
    }

    private static UpdateManifestEntry CreateEntry(string version = "2.0.0", long sizeBytes = 1024) => new() {
        Version = version,
        SizeBytes = sizeBytes,
        DownloadUrl = "https://example.com/download/jcc.exe",
        Sha256 = "a1b2c3",
    };

    [Fact]
    public async Task Execute_WithoutService_DegradesToManualGuidance() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => new UpgradeCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("当前版本: ");
        output.Should().Contain("请手动访问 GitHub Releases 获取最新版本",
            "升级服务缺失时应降级引导用户手动获取而非抛异常");
    }

    [Fact]
    public async Task Execute_DefaultCheck_ReportsNewerVersion() {
        var (cmd, upgrade, context) = Arrange(arguments: "");
        upgrade.Setup(u => u.GetLatestVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Version(2, 0, 0));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前版本: 1.0.0");
        output.Should().Contain("最新版本: 2.0.0");
        output.Should().Contain("有新版本可用! 使用 /upgrade download 下载，/upgrade apply 安装");
        upgrade.Verify(u => u.GetLatestVersionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_CheckWhenUpToDate_ShowsAlreadyLatest() {
        var (cmd, upgrade, context) = Arrange(arguments: "check");
        upgrade.Setup(u => u.GetLatestVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Version(1, 0, 0));

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("已是最新版本");
        output.Should().NotContain("有新版本可用");
    }

    [Fact]
    public async Task Execute_CheckWhenLatestUnknown_ShowsManualHint() {
        var (cmd, upgrade, context) = Arrange(arguments: "check");
        upgrade.Setup(u => u.GetLatestVersionAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((Version?)null);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("无法获取最新版本信息");
        output.Should().Contain("请手动访问 GitHub Releases 获取最新版本");
    }

    [Fact]
    public async Task Execute_CheckWhenServiceThrows_ShowsHandledError() {
        var (cmd, upgrade, context) = Arrange(arguments: "check");
        upgrade.Setup(u => u.GetLatestVersionAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("network down"));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("检查更新失败: network down", "服务异常应被捕获并输出操作名+错误信息而非中断命令");
    }

    [Theory]
    [InlineData(true, "有新版本可用")]
    [InlineData(false, "已是最新版本")]
    public async Task Execute_ForceCheck_ReportsAvailability(bool available, string expected) {
        var (cmd, upgrade, context) = Arrange(arguments: "force");
        upgrade.Setup(u => u.IsUpdateAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(available);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("正在强制检查更新...");
        output.Should().Contain(expected);
        upgrade.Verify(u => u.IsUpdateAvailableAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_DownloadWithoutUpdateEntry_ShowsNoSource() {
        var (cmd, upgrade, context) = Arrange(arguments: "download");
        upgrade.Setup(u => u.GetUpdateEntryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((UpdateManifestEntry?)null);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("无可用更新或未配置更新源");
        upgrade.Verify(u => u.DownloadUpdateAsync(It.IsAny<UpdateManifestEntry>(), It.IsAny<IProgress<UpdateDownloadProgress>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_DownloadSuccess_ShowsVersionSizeAndPath() {
        var (cmd, upgrade, context) = Arrange(arguments: "download");
        upgrade.Setup(u => u.GetUpdateEntryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEntry(sizeBytes: 2 * 1024 * 1024));
        upgrade.Setup(u => u.DownloadUpdateAsync(It.IsAny<UpdateManifestEntry>(), It.IsAny<IProgress<UpdateDownloadProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult { Success = true, DownloadedPath = "/tmp/jcc-2.0.0.exe" });

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("发现更新: 1.0.0 → 2.0.0 (2.0 MB)");
        output.Should().Contain("下载完成: /tmp/jcc-2.0.0.exe");
        output.Should().Contain("SHA256 校验通过");
        output.Should().Contain("使用 /upgrade apply 安装更新");
    }

    [Fact]
    public async Task Execute_DownloadFailure_ShowsErrorMessage() {
        var (cmd, upgrade, context) = Arrange(arguments: "download");
        upgrade.Setup(u => u.GetUpdateEntryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEntry());
        upgrade.Setup(u => u.DownloadUpdateAsync(It.IsAny<UpdateManifestEntry>(), It.IsAny<IProgress<UpdateDownloadProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult { Success = false, ErrorMessage = "checksum mismatch" });

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("下载失败: checksum mismatch");
    }

    [Fact]
    public async Task Execute_ApplySuccess_AnnouncesRestartRequirement() {
        var (cmd, upgrade, context) = Arrange(arguments: "apply");
        upgrade.Setup(u => u.GetUpdateEntryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEntry());
        upgrade.Setup(u => u.DownloadUpdateAsync(It.IsAny<UpdateManifestEntry>(), It.IsAny<IProgress<UpdateDownloadProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult { Success = true, DownloadedPath = "/tmp/jcc-2.0.0.exe" });
        upgrade.Setup(u => u.ApplyUpdateAsync("/tmp/jcc-2.0.0.exe", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult { Success = true, DownloadedPath = "/tmp/jcc-2.0.0.exe" });

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("下载完成，正在安装...");
        output.Should().Contain("更新安装成功! 请重启 jcc 生效");
        upgrade.Verify(u => u.ApplyUpdateAsync("/tmp/jcc-2.0.0.exe", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_ApplyFailure_ShowsInstallError() {
        var (cmd, upgrade, context) = Arrange(arguments: "apply");
        upgrade.Setup(u => u.GetUpdateEntryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateEntry());
        upgrade.Setup(u => u.DownloadUpdateAsync(It.IsAny<UpdateManifestEntry>(), It.IsAny<IProgress<UpdateDownloadProgress>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult { Success = true, DownloadedPath = "/tmp/jcc-2.0.0.exe" });
        upgrade.Setup(u => u.ApplyUpdateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult { Success = false, ErrorMessage = "permission denied" });

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("安装失败: permission denied");
    }

    [Fact]
    public async Task Execute_AutoWhenUpToDate_ShortsToLatestMessage() {
        var (cmd, upgrade, context) = Arrange(arguments: "auto");
        upgrade.Setup(u => u.IsUpdateAvailableAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("自动更新模式...");
        output.Should().Contain("已是最新版本");
        upgrade.Verify(u => u.GetUpdateEntryAsync(It.IsAny<CancellationToken>()), Times.Never,
            "无更新时 auto 不应继续获取清单");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsUsage() {
        var (cmd, upgrade, context) = Arrange(arguments: "bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知参数: bogus");
        output.Should().Contain("用法: /upgrade [check|force|download|apply|auto]");
        upgrade.Verify(u => u.GetLatestVersionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new UpgradeCommand();
        cmd.Name.Should().Be("upgrade");
        cmd.Usage.Should().StartWith("/upgrade");
        cmd.Aliases.Should().BeEmpty();
    }
}
