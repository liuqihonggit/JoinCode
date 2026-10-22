namespace Composition.Tests.Commands;

/// <summary>
/// /plugin 命令单元测试 — 覆盖 list(管理器缺失/空列表/工作流+外部插件渲染)/install(缺参用法/路径不存在/
/// 安装成功/自动加载列表持久化)/uninstall(缺参/未加载/成功/失败)/enable+disable(disabled 列表 JSON 双向持久化/
/// 幂等提示/配置服务缺失静默)/未知操作
/// <para>IPluginManager 经本地构造 CommandServices 注入;IConfigurationService 经 DI 注入</para>
/// </summary>
[Collection("command-output")]
public sealed class PluginCommandTest {
    private static (PluginCommand Command, Mock<IPluginManager> Manager, Mock<IConfigurationService> Config, IFileSystem Fs, ChatCommandContext Context) Arrange(
        string arguments = "",
        Mock<IPluginManager>? managerOverride = null,
        Mock<IConfigurationService>? configOverride = null,
        IFileSystem? fileSystem = null) {
        var manager = managerOverride ?? new Mock<IPluginManager>();
        if (managerOverride is null) {
            manager.Setup(m => m.LoadedWorkflowPluginNames).Returns([]);
            manager.Setup(m => m.LoadedExternalPluginNames).Returns([]);
        }
        var config = configOverride ?? new Mock<IConfigurationService>();
        var services = new ServiceCollection();
        services.AddSingleton(config.Object);
        var fs = fileSystem ?? TestFileSystem.Current;
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = fs,
            ServiceProvider = services.BuildServiceProvider(),
            PluginManager = manager.Object,
        };
        var context = CommandTestHarness.CreateContext(commandServices, arguments: arguments);
        return (new PluginCommand(), manager, config, fs, context);
    }

    [Fact]
    public async Task Execute_ListWithoutManager_ShowsManagerUninitialized() {
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
        };
        var context = CommandTestHarness.CreateContext(commandServices, arguments: "list");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => new PluginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("插件管理器未初始化");
    }

    [Fact]
    public async Task Execute_ListWithoutPlugins_ShowsEmptyHint() {
        var (cmd, _, _, _, context) = Arrange(arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前无已加载的插件");
        output.Should().Contain("使用 /plugin install <exe-path> 安装插件");
    }

    [Fact]
    public async Task Execute_ListWithPlugins_RendersWorkflowAndExternalPlugins() {
        var manager = new Mock<IPluginManager>();
        manager.Setup(m => m.LoadedWorkflowPluginNames).Returns(new List<string> { "wf-a" });
        manager.Setup(m => m.LoadedExternalPluginNames).Returns(new List<string> { "ext-b" });
        var (cmd, _, _, _, context) = Arrange(arguments: "list", managerOverride: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("插件列表:");
        output.Should().Contain("wf-a (工作流) — 已加载");
        output.Should().Contain("ext-b (外部) — 已加载");
    }

    [Fact]
    public async Task Execute_InstallWithoutPath_ShowsUsage() {
        var (cmd, _, _, _, context) = Arrange(arguments: "install");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /plugin install <exe-path>");
    }

    [Fact]
    public async Task Execute_InstallWithMissingPath_ShowsPathError() {
        var (cmd, manager, _, _, context) = Arrange(arguments: "install /no/such-tool.exe");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("插件路径不存在: /no/such-tool.exe");
        manager.Verify(m => m.LoadExternalPluginAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_InstallExistingPath_LoadsPluginWithoutConfig() {
        var fs = TestFileSystem.Current;
        await fs.WriteAllTextAsync("/plugins/tool-x.exe", "bin");
        var (cmd, manager, _, _, context) = Arrange(arguments: "install /plugins/tool-x.exe", fileSystem: fs);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已安装并加载插件: tool-x");
        manager.Verify(m => m.LoadExternalPluginAsync("/plugins/tool-x.exe", "tool-x", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_InstallExistingPath_AddsToAutoLoadList() {
        var fs = TestFileSystem.Current;
        await fs.WriteAllTextAsync("/plugins/tool-y.exe", "bin");
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("plugins.autoLoadExternalPlugins", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var (cmd, _, configMock, _, context) = Arrange(
            arguments: "install /plugins/tool-y.exe", configOverride: config, fileSystem: fs);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("已安装并加载插件: tool-y");
        output.Should().Contain("已添加到自动加载列表: tool-y");
        configMock.Verify(c => c.SetAsync(
            "plugins.autoLoadExternalPlugins",
            It.Is<string>(json => json.Contains("tool-y")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_UninstallWithoutName_ShowsUsage() {
        var (cmd, _, _, _, context) = Arrange(arguments: "uninstall");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /plugin uninstall <name>");
    }

    [Fact]
    public async Task Execute_UninstallNotLoaded_ShowsNotLoaded() {
        var (cmd, manager, _, _, context) = Arrange(arguments: "uninstall ghost");
        manager.Setup(m => m.IsPluginLoaded("ghost")).Returns(false);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("插件 'ghost' 未加载");
        manager.Verify(m => m.UnloadPluginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_UninstallLoaded_ConfirmsUnload() {
        var (cmd, manager, _, _, context) = Arrange(arguments: "uninstall wf-a");
        manager.Setup(m => m.IsPluginLoaded("wf-a")).Returns(true);
        manager.Setup(m => m.UnloadPluginAsync("wf-a", It.IsAny<PluginUnloadOptions?>()))
            .ReturnsAsync(PluginUnloadResult.Success("wf-a", TimeSpan.Zero));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已卸载插件: wf-a");
    }

    [Fact]
    public async Task Execute_UnloadFailure_ShowsErrorDetail() {
        var (cmd, manager, _, _, context) = Arrange(arguments: "uninstall wf-b");
        manager.Setup(m => m.IsPluginLoaded("wf-b")).Returns(true);
        manager.Setup(m => m.UnloadPluginAsync("wf-b", It.IsAny<PluginUnloadOptions?>()))
            .ReturnsAsync(PluginUnloadResult.Failure("wf-b", TimeSpan.Zero, "alc unload failed"));

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("卸载插件失败: wf-b - alc unload failed");
    }

    [Fact]
    public async Task Execute_EnableWithoutName_ShowsUsage() {
        var (cmd, _, _, _, context) = Arrange(arguments: "enable");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /plugin enable <name>");
    }

    [Fact]
    public async Task Execute_DisableNewPlugin_PersistsDisabledList() {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("plugins.disabledPlugins", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var (cmd, _, configMock, _, context) = Arrange(arguments: "disable tool-x", configOverride: config);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已禁用插件: tool-x（重启后生效）");
        configMock.Verify(c => c.SetAsync(
            "plugins.disabledPlugins",
            It.Is<string>(json => json.Contains("tool-x")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_DisableAlreadyDisabled_ShowsIdempotentHint() {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("plugins.disabledPlugins", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""["tool-x"]""");
        var (cmd, _, configMock, _, context) = Arrange(arguments: "disable tool-x", configOverride: config);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("插件 'tool-x' 已被禁用");
        configMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "重复禁用不应重复写配置");
    }

    [Fact]
    public async Task Execute_EnableDisabledPlugin_RemovesFromDisabledList() {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("plugins.disabledPlugins", It.IsAny<CancellationToken>()))
            .ReturnsAsync("""["tool-x"]""");
        var (cmd, _, configMock, _, context) = Arrange(arguments: "enable tool-x", configOverride: config);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已启用插件: tool-x（重启后生效）");
        configMock.Verify(c => c.SetAsync(
            "plugins.disabledPlugins",
            It.Is<string>(json => !json.Contains("tool-x")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_EnableNotDisabled_ShowsNotDisabledHint() {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("plugins.disabledPlugins", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var (cmd, _, _, _, context) = Arrange(arguments: "enable tool-x", configOverride: config);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("插件 'tool-x' 未被禁用");
    }

    [Fact]
    public async Task Execute_ToggleWithoutConfigService_SilentlyDegrades() {
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
        };
        var context = CommandTestHarness.CreateContext(commandServices, arguments: "enable tool-x");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => new PluginCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().NotContain("已启用插件", "配置服务缺失时(非交互环境)应静默降级不输出状态变更");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsSupportedActions() {
        var (cmd, _, _, _, context) = Arrange(arguments: "bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知操作: bogus");
        output.Should().Contain("支持: list, install, uninstall, enable, disable");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new PluginCommand();
        cmd.Name.Should().Be("plugin");
        cmd.Usage.Should().StartWith("/plugin");
        cmd.Aliases.Should().Contain("plugins");
        cmd.Aliases.Should().Contain("marketplace");
    }
}
