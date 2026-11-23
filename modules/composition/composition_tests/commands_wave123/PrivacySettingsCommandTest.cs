namespace Composition.Tests.Commands;

/// <summary>
/// /privacy-settings 命令单元测试 — 覆盖 show(空参默认/show 关键字/配置服务缺失降级)/
/// telemetry on-off/analytics off/crash-reports on 三开关持久化/缺值用法提示/未知设置
/// </summary>
[Collection("command-output")]
public sealed class PrivacySettingsCommandTest {
    private static (PrivacySettingsCommand Command, ChatCommandContext Context, Mock<IConfigurationService> Config) Arrange(
        string arguments,
        Mock<IConfigurationService>? configService = null) {
        var config = configService ?? new Mock<IConfigurationService>();
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(config.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new PrivacySettingsCommand(), context, config);
    }

    [Fact]
    public async Task Execute_EmptyArgs_ShowsAllSettingsWithDefaults() {
        var (cmd, context, _) = Arrange("");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("隐私设置:");
        output.Should().Contain("遥测:").And.Contain("未启用");
        output.Should().Contain("分析:").And.Contain("未启用");
        output.Should().Contain("崩溃报告:").And.Contain("未启用");
    }

    [Fact]
    public async Task Execute_ShowKeyword_DisplaysConfiguredValues() {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("privacy.telemetry", It.IsAny<CancellationToken>()))
            .ReturnsAsync("on");
        config.Setup(c => c.GetAsync("privacy.analytics", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        config.Setup(c => c.GetAsync("privacy.crashReports", It.IsAny<CancellationToken>()))
            .ReturnsAsync("off");
        var (cmd, context, _) = Arrange("show", config);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("遥测:").And.Contain("已启用", "配置值 on 应显示已启用");
        output.Should().Contain("分析:").And.Contain("未启用", "null 值应回退默认 off");
        output.Should().Contain("崩溃报告:").And.Contain("未启用");
    }

    [Fact]
    public async Task Execute_TelemetryOn_PersistsAndConfirms() {
        var (cmd, context, config) = Arrange("telemetry on");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync("privacy.telemetry", "on", It.IsAny<CancellationToken>()), Times.Once,
            "telemetry on 应写入配置服务");
        output.Should().Contain("privacy.telemetry: 已启用");
    }

    [Fact]
    public async Task Execute_AnalyticsOff_PersistsAndConfirms() {
        var (cmd, context, config) = Arrange("analytics off");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync("privacy.analytics", "off", It.IsAny<CancellationToken>()), Times.Once);
        output.Should().Contain("privacy.analytics: 已禁用");
    }

    [Fact]
    public async Task Execute_CrashReportsOn_PersistsAndConfirms() {
        var (cmd, context, config) = Arrange("crash-reports on");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync("privacy.crashReports", "on", It.IsAny<CancellationToken>()), Times.Once);
        output.Should().Contain("privacy.crashReports: 已启用");
    }

    [Fact]
    public async Task Execute_SettingWithoutValue_ShowsUsageWithoutPersisting() {
        var (cmd, context, config) = Arrange("telemetry");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "缺 on/off 值时不应写入配置");
        output.Should().Contain("用法: /privacy-settings telemetry on|off");
    }

    [Fact]
    public async Task Execute_UnknownSetting_ShowsSupportedKeys() {
        var (cmd, context, _) = Arrange("bogus on");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知设置: bogus on");
        output.Should().Contain("支持: telemetry, analytics, crash-reports",
            "错误提示应引导用户使用支持的设置项");
    }

    [Fact]
    public async Task Execute_WithoutConfigService_ShowStillWorks() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new PrivacySettingsCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("隐私设置:", "配置服务缺失时应回退默认值仍可查看");
        output.Should().Contain("未启用");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new PrivacySettingsCommand();
        cmd.Name.Should().Be("privacy-settings");
        cmd.Usage.Should().StartWith("/privacy-settings");
    }
}
