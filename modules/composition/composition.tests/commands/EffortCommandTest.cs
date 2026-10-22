namespace Composition.Tests.Commands;

/// <summary>
/// /effort 命令单元测试 — 覆盖 help/空参与 status 显示当前级别/auto 清除自定义/
/// 无效参数/有效级别更新+持久化/数字别名/xhigh 别名→max/max 不支持模型自动降级 high/
/// max 支持模型正常生效
/// <para>statusBar 用真实 StatusBarData,settingsProvider 用 Moq 属性桩双向断言</para>
/// </summary>
[Collection("command-output")]
public sealed class EffortCommandTest {
    private static (EffortCommand Command, ChatCommandContext Context, Mock<IExecutionSettingsProvider> Settings, StatusBarData StatusBar, Mock<IConfigurationService> Config) Arrange(
        string arguments,
        EffortLevel initialLevel = EffortLevel.Auto,
        Mock<IModelCatalog>? modelCatalog = null,
        Mock<IFastModeService>? fastMode = null,
        bool withConfigService = true) {
        var settings = new Mock<IExecutionSettingsProvider>();
        settings.SetupProperty(p => p.EffortLevel, initialLevel);
        var statusBar = new StatusBarData();
        var config = new Mock<IConfigurationService>();
        var fastModeMock = fastMode ?? new Mock<IFastModeService>();
        var services = CommandTestHarness.CreateServices(
            statusBarData: statusBar,
            executionSettingsProvider: settings.Object,
            configureDi: di => {
                if (withConfigService) di.AddSingleton(config.Object);
                di.AddSingleton(fastModeMock.Object);
                if (modelCatalog is not null) di.AddSingleton(modelCatalog.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new EffortCommand(), context, settings, statusBar, config);
    }

    [Fact]
    public async Task Execute_Help_ShowsDetailedUsage() {
        var (cmd, context, _, _, _) = Arrange("help");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /effort [low|medium|high|max|auto|unset]");
        output.Should().Contain("low    - 快速，简洁实现");
        output.Should().Contain("max    - 最深推理，仅 Opus 级别模型");
    }

    [Theory]
    [InlineData("")]
    [InlineData("current")]
    [InlineData("status")]
    public async Task Execute_CurrentKeywords_ShowCurrentAutoLevel(string arguments) {
        var (cmd, context, _, _, _) = Arrange(arguments);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前推理力度: auto (模型默认级别)");
        output.Should().Contain("使用 /effort low|medium|high|max|auto 调整");
    }

    [Fact]
    public async Task Execute_Current_WithProviderLevel_ShowsProviderLevel() {
        var (cmd, context, _, _, _) = Arrange("status", initialLevel: EffortLevel.High);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("当前推理力度: high (全面，广泛测试)");
    }

    [Fact]
    public async Task Execute_Auto_ClearsCustomLevel() {
        var (cmd, context, settings, statusBar, _) = Arrange("auto", initialLevel: EffortLevel.High);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("推理力度: auto (使用模型默认级别)");
        settings.Object.EffortLevel.Should().Be(EffortLevel.Auto, "auto 应重置执行设置为 Auto");
        statusBar.EffortLevel.Should().Be(EffortLevel.Auto, "auto 应同步状态栏级别");
    }

    [Fact]
    public async Task Execute_Unset_ClearsCustomLevelSameAsAuto() {
        var (cmd, context, settings, _, _) = Arrange("unset", initialLevel: EffortLevel.Low);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("推理力度: auto (使用模型默认级别)", "unset 与 auto 语义一致,均清除自定义设置");
        settings.Object.EffortLevel.Should().Be(EffortLevel.Auto);
    }

    [Fact]
    public async Task Execute_InvalidArgument_ShowsErrorWithValidOptions() {
        var (cmd, context, _, _, _) = Arrange("bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("无效参数: bogus。有效选项: low, medium, high, max, auto",
            "错误提示应列出全部有效选项引导用户");
    }

    [Fact]
    public async Task Execute_ValidLevel_UpdatesStatusBarAndSettings() {
        var (cmd, context, settings, statusBar, _) = Arrange("low");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("推理力度: low (快速，简洁实现)");
        settings.Object.EffortLevel.Should().Be(EffortLevel.Low, "有效级别应写入执行设置");
        statusBar.EffortLevel.Should().Be(EffortLevel.Low, "有效级别应同步状态栏");
    }

    [Fact]
    public async Task Execute_ValidLevel_PersistsToConfigService() {
        var (cmd, context, _, _, config) = Arrange("medium");

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync("effortLevel", "medium", It.IsAny<CancellationToken>()), Times.Once,
            "有效级别应持久化到配置服务(键为 effortLevel)");
    }

    [Fact]
    public async Task Execute_Auto_RemovesPersistedLevel() {
        var (cmd, context, _, _, config) = Arrange("auto");

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.RemoveAsync("effortLevel", It.IsAny<CancellationToken>()), Times.Once,
            "auto 清除自定义级别时应移除配置项而非写入");
        config.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Execute_NumericAliasThree_ResolvesHigh() {
        var (cmd, context, settings, _, _) = Arrange("3");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("推理力度: high (全面，广泛测试)", "数字别名 3 应解析为 high");
        settings.Object.EffortLevel.Should().Be(EffortLevel.High);
    }

    [Fact]
    public async Task Execute_XHighAlias_ResolvesMaxWhenSupported() {
        var catalog = new Mock<IModelCatalog>();
        catalog.Setup(c => c.SupportsMaxEffort(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var fastMode = new Mock<IFastModeService>();
        fastMode.Setup(f => f.PrimaryModelId).Returns("claude-opus-4");
        var (cmd, context, settings, _, _) = Arrange("xhigh", modelCatalog: catalog, fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("推理力度: max (最深推理，仅 Opus 级别)", "xhigh 别名应解析为 max");
        settings.Object.EffortLevel.Should().Be(EffortLevel.Max);
    }

    [Fact]
    public async Task Execute_MaxOnUnsupportedModel_DowngradesToHigh() {
        var catalog = new Mock<IModelCatalog>();
        catalog.Setup(c => c.SupportsMaxEffort(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
        var fastMode = new Mock<IFastModeService>();
        fastMode.Setup(f => f.PrimaryModelId).Returns("gpt-4o-mini");
        var (cmd, context, settings, _, _) = Arrange("max", modelCatalog: catalog, fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("模型 gpt-4o-mini 不支持 max effort，已降级为 high",
            "不支持 max 的模型应自动降级为 high");
        settings.Object.EffortLevel.Should().Be(EffortLevel.High, "降级后执行设置应为 high");
    }

    /// <summary>生产 bug 修复（TASK065 代理 C 登记）：max + ModelCatalog 未注册（精简启动形态）时
    /// ResolveModelCatalog 抛 [APP002] 未捕获异常传播 REPL — 修复为保守降级 high 不抛
    ///（对齐"无法验证 max 支持性即不给 max"的降级语义，而非命令崩溃）。</summary>
    [Fact]
    public async Task Execute_MaxWithoutModelCatalog_DowngradesInsteadOfThrowing() {
        var fastMode = new Mock<IFastModeService>();
        fastMode.Setup(f => f.PrimaryModelId).Returns("sensenova-6.8-flash-lite");
        var (cmd, context, settings, _, _) = Arrange("max", modelCatalog: null, fastMode: fastMode);

        var act = async () => {
            var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));
            result.ShouldContinue.Should().BeTrue("目录缺失属降级场景非命令失败");
        };

        await act.Should().NotThrowAsync("ModelCatalog 缺失应保守降级而非未捕获异常传播 REPL");
        settings.Object.EffortLevel.Should().Be(EffortLevel.High, "无法验证支持性时保守降级为 high");
    }

    [Fact]
    public async Task Execute_MaxOnSupportedModel_AppliesMax() {
        var catalog = new Mock<IModelCatalog>();
        catalog.Setup(c => c.SupportsMaxEffort(It.IsAny<string>(), It.IsAny<string>())).Returns(true);
        var fastMode = new Mock<IFastModeService>();
        fastMode.Setup(f => f.PrimaryModelId).Returns("claude-opus-4");
        var (cmd, context, settings, _, _) = Arrange("max", modelCatalog: catalog, fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("推理力度: max (最深推理，仅 Opus 级别)");
        settings.Object.EffortLevel.Should().Be(EffortLevel.Max);
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new EffortCommand();
        cmd.Name.Should().Be("effort");
        cmd.Usage.Should().StartWith("/effort");
        cmd.ArgumentHint.Should().Be("[low|medium|high|max|auto|unset]");
    }
}
