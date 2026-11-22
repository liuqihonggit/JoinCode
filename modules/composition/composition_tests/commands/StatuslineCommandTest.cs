namespace Composition.Tests.Commands;

/// <summary>
/// /statusline 命令单元测试 — 覆盖 on/enable 启用写入配置/off/disable 禁用写入配置/
/// format 查询当前模板/format 设置模板持久化/空参状态汇总(配置读取+默认值回退)/配置服务缺失降级
/// <para>IConfigurationService 经 DI 注入;断言 SetAsync 键值对与输出不变量</para>
/// </summary>
[Collection("command-output")]
public sealed class StatuslineCommandTest {
    private static (StatuslineCommand Command, Mock<IConfigurationService> Config, ChatCommandContext Context) Arrange(
        string arguments = "",
        bool withConfig = true,
        string? enabledValue = null,
        string? formatValue = null) {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("statusline.enabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync(enabledValue);
        config.Setup(c => c.GetAsync("statusline.format", It.IsAny<CancellationToken>()))
            .ReturnsAsync(formatValue);

        var services = CommandTestHarness.CreateServices(
            configureDi: di => {
                if (withConfig) di.AddSingleton(config.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new StatuslineCommand(), config, context);
    }

    [Fact]
    public async Task Execute_On_WritesEnabledTrue() {
        var (cmd, config, context) = Arrange(arguments: "on");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        config.Verify(c => c.SetAsync("statusline.enabled", "true", It.IsAny<CancellationToken>()), Times.Once,
            "on 应写入 statusline.enabled=true");
        output.Should().Contain("状态栏: 已启用");
    }

    [Fact]
    public async Task Execute_EnableAlias_SameAsOn() {
        var (cmd, config, context) = Arrange(arguments: "enable");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync("statusline.enabled", "true", It.IsAny<CancellationToken>()), Times.Once);
        output.Should().Contain("状态栏: 已启用");
    }

    [Fact]
    public async Task Execute_Off_WritesEnabledFalse() {
        var (cmd, config, context) = Arrange(arguments: "off");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync("statusline.enabled", "false", It.IsAny<CancellationToken>()), Times.Once,
            "off 应写入 statusline.enabled=false");
        output.Should().Contain("状态栏: 已禁用");
    }

    [Fact]
    public async Task Execute_DisableAlias_SameAsOff() {
        var (cmd, config, context) = Arrange(arguments: "disable");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync("statusline.enabled", "false", It.IsAny<CancellationToken>()), Times.Once);
        output.Should().Contain("状态栏: 已禁用");
    }

    [Fact]
    public async Task Execute_Format_WithoutTemplate_ShowsCurrentFormatAndUsage() {
        var (cmd, config, context) = Arrange(arguments: "format", formatValue: "{model} | {tokens}");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.GetAsync("statusline.format", It.IsAny<CancellationToken>()), Times.Once,
            "format 查询应读取当前模板");
        output.Should().Contain("当前状态栏格式: {model} | {tokens}");
        output.Should().Contain("用法: /statusline format <template>");
        output.Should().Contain("可用变量: {model}, {tokens}, {cost}, {mode}, {time}");
        config.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "仅查询时不应写入配置");
    }

    [Fact]
    public async Task Execute_Format_WithoutTemplate_NoStoredFormat_ShowsDefault() {
        var (cmd, _, context) = Arrange(arguments: "format", formatValue: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前状态栏格式: (默认)");
    }

    [Fact]
    public async Task Execute_Format_WithTemplate_PersistsTemplate() {
        var (cmd, config, context) = Arrange(arguments: "format {tokens} used");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync("statusline.format", "{tokens} used", It.IsAny<CancellationToken>()), Times.Once,
            "format 设置应持久化模板");
        output.Should().Contain("状态栏格式: {tokens} used");
    }

    [Fact]
    public async Task Execute_EmptyArgs_ShowsCurrentStatusWithFormat() {
        var (cmd, _, context) = Arrange(arguments: "", enabledValue: "true", formatValue: "{model}");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("状态栏: 已启用");
        output.Should().Contain("格式: {model}");
        output.Should().Contain("使用 /statusline on|off 切换状态栏");
        output.Should().Contain("使用 /statusline format <template> 设置格式");
    }

    [Fact]
    public async Task Execute_EmptyArgs_DisabledByConfig_ShowsDisabled() {
        var (cmd, _, context) = Arrange(arguments: "", enabledValue: "false");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("状态栏: 已禁用");
    }

    [Fact]
    public async Task Execute_EmptyArgs_WithoutConfig_FallsBackToEnabledDefault() {
        var (cmd, config, context) = Arrange(arguments: "", withConfig: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("配置服务缺失应优雅降级");
        output.Should().Contain("状态栏: 已启用", "配置缺失时启用状态默认回退 true");
        output.Should().NotContain("格式:", "无配置服务时不应渲染格式行");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new StatuslineCommand();
        cmd.Name.Should().Be("statusline");
        cmd.Usage.Should().Be("/statusline [on|off|format]");
    }
}
