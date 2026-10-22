namespace Composition.Tests.Commands;

/// <summary>
/// /theme 命令单元测试 — 覆盖有效主题切换(dark/light/值域全覆盖)/大小写归一/show 交互回退纯文本列表/
/// 当前主题标记(*)/无效主题引导/配置服务缺失降级(仍应用主题)
/// <para>IConfigurationService 经 DI 容器解析,用 Moq 注入;交互选择器在测试环境走非交互纯文本回退分支</para>
/// </summary>
[Collection("command-output")]
public sealed class ThemeCommandTest {
    private static (ThemeCommand Command, Mock<IConfigurationService> Config, ChatCommandContext Context) Arrange(
        string arguments = "",
        Mock<IConfigurationService>? configOverride = null,
        string? currentTheme = null) {
        var config = configOverride ?? new Mock<IConfigurationService>();
        if (currentTheme is not null) {
            config.Setup(c => c.GetAsync(ConfigKeyEnumConstants.Theme, It.IsAny<CancellationToken>()))
                .ReturnsAsync(currentTheme);
        }
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(config.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new ThemeCommand(), config, context);
    }

    [Fact]
    public async Task Execute_DarkTheme_SetsConfigAndAnnounces() {
        var (cmd, config, context) = Arrange(arguments: "dark");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("主题已设置为：深色模式");
        config.Verify(c => c.SetAsync(ConfigKeyEnumConstants.Theme, "dark", It.IsAny<CancellationToken>()), Times.Once,
            "dark 主题应写入 theme 配置键");
    }

    [Theory]
    [InlineData("auto", "自动（匹配终端）")]
    [InlineData("light", "浅色模式")]
    [InlineData("dark-daltonized", "深色模式（色盲友好）")]
    [InlineData("light-daltonized", "浅色模式（色盲友好）")]
    [InlineData("dark-ansi", "深色模式（仅 ANSI 颜色）")]
    [InlineData("light-ansi", "浅色模式（仅 ANSI 颜色）")]
    public async Task Execute_KnownThemes_ValueDomainCoverage(string theme, string expectedLabel) {
        var (cmd, config, context) = Arrange(arguments: theme);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain($"主题已设置为：{expectedLabel}", "主题名应命中 [ChatCommandArg] 声明的枚举值域");
        config.Verify(c => c.SetAsync(ConfigKeyEnumConstants.Theme, theme, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_ThemeNameIsCaseInsensitive() {
        var (cmd, config, context) = Arrange(arguments: "DARK");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("主题已设置为：深色模式", "主题名应 ToLowerInvariant 归一匹配");
        config.Verify(c => c.SetAsync(ConfigKeyEnumConstants.Theme, "dark", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_ShowWithoutArgs_RendersNonInteractiveThemeList() {
        var (cmd, _, context) = Arrange(arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前主题：自动（匹配终端）", "无配置服务时当前主题回退 auto");
        output.Should().Contain("可用主题：");
        output.Should().Contain("dark");
        output.Should().Contain("light-daltonized");
        output.Should().Contain("用法：/theme <名称>");
    }

    [Fact]
    public async Task Execute_ShowWithCurrentTheme_MarksActiveWithStar() {
        var (cmd, _, context) = Arrange(arguments: "show", currentTheme: "dark");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("当前主题：深色模式");
        output.Should().Contain("深色模式 *", "当前激活主题应带 * 标记");
    }

    [Fact]
    public async Task Execute_UnknownTheme_ShowsErrorAndPickerFallback() {
        var (cmd, config, context) = Arrange(arguments: "neon-pink");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知主题：neon-pink");
        output.Should().Contain("可用主题：", "未知主题应回退展示主题列表引导用户");
        config.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "无效主题不应写入配置");
    }

    [Fact]
    public async Task Execute_WithoutConfigService_StillAppliesThemeLocally() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "light");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => new ThemeCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("主题已设置为：浅色模式", "配置服务缺失时应降级为仅本地应用主题,不抛异常");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ThemeCommand();
        cmd.Name.Should().Be("theme");
        cmd.Usage.Should().StartWith("/theme");
        cmd.Aliases.Should().BeEmpty();
    }
}
