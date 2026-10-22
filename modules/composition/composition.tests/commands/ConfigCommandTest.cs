namespace Composition.Tests.Commands;

/// <summary>
/// /config 命令单元测试 — 覆盖 get(取值/键不存在/缺参用法)/set(写入+已知键描述/缺参用法/remove(成功/不存在/别名)/
/// list 默认(空配置+已有配置渲染)/服务缺失抛 [APP004]
/// <para>IConfigurationService 经 DI 容器解析,用 Moq 注入</para>
/// </summary>
[Collection("command-output")]
public sealed class ConfigCommandTest {
    private static (ConfigCommand Command, Mock<IConfigurationService> Config, ChatCommandContext Context) Arrange(
        string arguments = "",
        Dictionary<string, string>? allConfig = null) {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(allConfig ?? []);
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(config.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new ConfigCommand(), config, context);
    }

    [Fact]
    public async Task Execute_GetExistingKey_ShowsKeyValue() {
        var (cmd, config, context) = Arrange(arguments: "get theme");
        config.Setup(c => c.GetAsync("theme", It.IsAny<CancellationToken>())).ReturnsAsync("dark");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("theme = dark");
    }

    [Fact]
    public async Task Execute_GetMissingKey_ShowsNotExistMessage() {
        var (cmd, config, context) = Arrange(arguments: "get nope");
        config.Setup(c => c.GetAsync("nope", It.IsAny<CancellationToken>())).ReturnsAsync((string?)null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("配置项 'nope' 不存在");
    }

    [Fact]
    public async Task Execute_GetWithoutKey_ShowsUsage() {
        var (cmd, config, context) = Arrange(arguments: "get");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /config get <key>");
        config.Verify(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "缺参时不应触发任何读取");
    }

    [Fact]
    public async Task Execute_SetKnownKey_PersistsAndShowsDescription() {
        var (cmd, config, context) = Arrange(arguments: "set theme dark");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已设置: theme = dark");
        output.Should().Contain("theme: UI 主题 (dark/light/auto)", "已知配置项应展示元数据描述");
        config.Verify(c => c.SetAsync("theme", "dark", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_SetWithSpacesInValue_JoinsRemainingArgs() {
        var (cmd, config, context) = Arrange(arguments: "set theme dark mode");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("已设置: theme = dark mode", "value 应合并空格后的全部参数");
        config.Verify(c => c.SetAsync("theme", "dark mode", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_SetWithoutValue_ShowsUsage() {
        var (cmd, config, context) = Arrange(arguments: "set theme");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /config set <key> <value>");
        config.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_RemoveExistingKey_ConfirmsRemoval() {
        var (cmd, config, context) = Arrange(arguments: "remove theme");
        config.Setup(c => c.RemoveAsync("theme", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已移除: theme");
        config.Verify(c => c.RemoveAsync("theme", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("remove")]
    [InlineData("delete")]
    [InlineData("rm")]
    public async Task Execute_RemoveAliases_AllResolveToRemove(string alias) {
        var (cmd, config, context) = Arrange(arguments: $"{alias} theme");
        config.Setup(c => c.RemoveAsync("theme", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("已移除: theme", $"别名 {alias} 应归一到 remove 语义");
    }

    [Fact]
    public async Task Execute_RemoveMissingKey_ShowsWarning() {
        var (cmd, config, context) = Arrange(arguments: "remove nope");
        config.Setup(c => c.RemoveAsync("nope", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("配置项 'nope' 不存在");
    }

    [Fact]
    public async Task Execute_RemoveWithoutKey_ShowsUsage() {
        var (cmd, _, context) = Arrange(arguments: "remove");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /config remove <key>");
    }

    [Fact]
    public async Task Execute_EmptyArgs_DefaultsToListWithEmptyConfig() {
        var (cmd, config, context) = Arrange(arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once, "空参默认走 list");
        output.Should().Contain("当前配置");
        output.Should().Contain("当前没有配置项");
        output.Should().Contain("已知配置项");
    }

    [Fact]
    public async Task Execute_List_ShowsSortedConfigWithDescriptions() {
        var (cmd, _, context) = Arrange(
            arguments: "ls",
            allConfig: new Dictionary<string, string> {
                ["theme"] = "dark",
                ["language"] = "zh",
                ["custom.key"] = "custom-value",
            });

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("custom.key = custom-value");
        output.Should().Contain("language = zh");
        output.Should().Contain("theme = dark");
        output.Should().Contain("(UI 主题 (dark/light/auto))", "已知键应附带括号内描述");
        output.Should().Contain("使用 /config set <key> <value> 设置配置");
    }

    [Fact]
    public async Task Execute_UnknownAction_FallsBackToList() {
        var (cmd, config, context) = Arrange(arguments: "bogus");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        config.Verify(c => c.GetAllAsync(It.IsAny<CancellationToken>()), Times.Once, "未知 action 应回退 list 分支");
        output.Should().Contain("当前没有配置项");
    }

    [Fact]
    public async Task Execute_WithoutConfigService_ThrowsApp004() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "get theme");

        var act = () => CommandTestHarness.CaptureOutputAsync(() => new ConfigCommand().ExecuteAsync(context));

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("*[APP004]*")
            .WithMessage("*IConfigurationService*");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ConfigCommand();
        cmd.Name.Should().Be("config");
        cmd.Usage.Should().StartWith("/config");
        cmd.Aliases.Should().BeEmpty();
    }
}
