// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace Composition.Tests.Commands;

/// <summary>
/// /sandbox-toggle 命令单元测试 — 覆盖 status 默认分支(禁用态/活动沙箱详情)/on+off 非交互确认拒绝降级/
/// switch(缺类型用法/无效类型/有效类型切换/管理器缺失)/exclude(缺参列表/路径不存在/新增持久化/
/// 重复路径幂等/配置服务缺失)/管理器缺失静默降级/混合大小写路径保留与大写子命令分派(TASK065 第五波修复)
/// <para>ISandboxManager 与 IConfigurationService 经 DI 注入;确认走静态 Confirmation,
/// 测试环境非交互恒拒绝 — 断言以硬编码中文文案+参数不变量为主</para>
/// </summary>
[Collection("command-output")]
public sealed class SandboxToggleCommandTest {
    private static SandboxInfo CreateInfo(string rootPath = @"C:\sandbox-root") => new() {
        Type = SandboxType.Soft,
        SandboxId = "sb-1",
        RootPath = rootPath,
        EnteredAt = new DateTime(2026, 1, 1, 10, 0, 0),
        IsRestricted = true,
    };

    private static (SandboxToggleCommand Command, Mock<ISandboxManager> Manager, Mock<IConfigurationService> Config, ChatCommandContext Context, IFileSystem Fs) Arrange(
        string arguments = "",
        Mock<ISandboxManager>? managerOverride = null,
        Mock<IConfigurationService>? configOverride = null,
        bool withManager = true,
        bool withConfig = true,
        SandboxInfo? currentSandbox = null,
        bool isInSandbox = false) {
        var manager = managerOverride ?? new Mock<ISandboxManager>();
        manager.Setup(m => m.GetAvailableTypes()).Returns([SandboxType.Soft, SandboxType.Docker]);
        manager.Setup(m => m.IsInSandbox).Returns(isInSandbox);
        manager.Setup(m => m.CurrentSandbox).Returns(currentSandbox);
        manager.Setup(m => m.ActiveSandboxType).Returns(SandboxType.Soft);

        var config = configOverride ?? new Mock<IConfigurationService>();
        var fs = TestFileSystem.Current;
        var services = CommandTestHarness.CreateServices(
            fileSystem: fs,
            configureDi: di => {
                if (withManager) di.AddSingleton(manager.Object);
                if (withConfig) di.AddSingleton(config.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new SandboxToggleCommand(), manager, config, context, fs);
    }

    [Fact]
    public async Task Execute_EmptyArgs_ShowsStatusWithDisabledState() {
        var (cmd, manager, _, context, _) = Arrange();

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("沙箱模式");
        output.Should().Contain("已禁用");
        output.Should().Contain("可用沙箱类型: soft, docker");
        output.Should().Contain("/sandbox-toggle on");
        output.Should().Contain("/sandbox-toggle exclude <path>");
        output.Should().Contain("/sandbox-toggle switch <soft|process|docker|bubblewrap>");
    }

    [Fact]
    public async Task Execute_StatusArg_SameAsDefault() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "status");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("可用沙箱类型: soft, docker");
        manager.Verify(m => m.EnterSandboxAsync(It.IsAny<SandboxOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_StatusWithActiveSandbox_ShowsSandboxDetails() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "status", currentSandbox: CreateInfo(), isInSandbox: true);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已启用");
        output.Should().Contain(@"C:\sandbox-root", "活动沙箱应显示根路径");
        output.Should().Contain("10:00:00", "活动沙箱应显示进入时间");
    }

    [Fact]
    public async Task Execute_On_NonInteractiveConfirmationDenied_SkipsEnter() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "on");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.EnterSandboxAsync(It.IsAny<SandboxOptions>(), It.IsAny<CancellationToken>()),
            Times.Never, "测试环境确认恒拒绝,不应进入沙箱");
        output.Should().NotBeEmpty("确认取消应有提示输出");
    }

    [Fact]
    public async Task Execute_EnableAlias_SameAsOn() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "enable");

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.EnterSandboxAsync(It.IsAny<SandboxOptions>(), It.IsAny<CancellationToken>()), Times.Never,
            "enable 别名应与 on 同样受确认门控");
    }

    [Fact]
    public async Task Execute_Off_NonInteractiveConfirmationDenied_SkipsExit() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "off");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.ExitSandboxAsync(It.IsAny<CancellationToken>()), Times.Never,
            "测试环境确认恒拒绝,不应退出沙箱");
        output.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Execute_WithoutManager_NonInteractive_SilentContinue() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "", withManager: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().NotContain("沙箱模式", "非交互环境管理器缺失不应输出状态面板");
        manager.Verify(m => m.EnterSandboxAsync(It.IsAny<SandboxOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_Switch_WithoutType_ShowsCurrentTypeAndUsage() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "switch");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前沙箱类型: soft");
        output.Should().Contain("可用类型: soft, docker");
        output.Should().Contain("用法: /sandbox-toggle switch <soft|process|docker|bubblewrap>");
        manager.Verify(m => m.SwitchProviderAsync(It.IsAny<SandboxType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_Switch_InvalidType_ShowsErrorWithAvailableTypes() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "switch bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知沙箱类型: bogus");
        output.Should().Contain("可用类型: soft, docker");
        manager.Verify(m => m.SwitchProviderAsync(It.IsAny<SandboxType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_Switch_ValidType_SwitchesProvider() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "switch docker");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.SwitchProviderAsync(SandboxType.Docker, It.IsAny<CancellationToken>()), Times.Once,
            "switch docker 应调用 SwitchProviderAsync(Docker)");
        output.Should().Contain("沙箱已切换到: docker");
    }

    [Fact]
    public async Task Execute_Switch_WithoutManager_ShowsUninitialized() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "switch docker", withManager: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("沙箱服务未初始化");
        manager.Verify(m => m.SwitchProviderAsync(It.IsAny<SandboxType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_Exclude_WithoutPath_ShowsCurrentExclusions() {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("sandbox.excludedPaths", It.IsAny<CancellationToken>()))
            .ReturnsAsync(@"C:\path-a");
        var (cmd, _, _, context, _) = Arrange(arguments: "exclude", configOverride: config);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.GetAsync("sandbox.excludedPaths", It.IsAny<CancellationToken>()), Times.Once,
            "exclude 无参时应读取现有排除路径");
        output.Should().Contain(@"C:\path-a", "现有排除路径应渲染");
        output.Should().Contain("/sandbox-toggle exclude <path>");
    }

    [Fact]
    public async Task Execute_Exclude_MissingPath_ShowsPathNotFound() {
        var config = new Mock<IConfigurationService>();
        var (cmd, _, configMock, context, _) = Arrange(
            arguments: @"exclude c:\no-such-dir", configOverride: config);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain(@"c:\no-such-dir", "路径不存在提示应含原始路径");
        configMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "路径不存在时不应写配置");
    }

    [Fact]
    public async Task Execute_Exclude_ExistingDirectory_PersistsFullPath() {
        var dir = Path.Combine(Path.GetTempPath(), "sb-exclude-new").ToLowerInvariant();
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("sandbox.excludedPaths", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var (cmd, _, _, context, fs) = Arrange(arguments: $"exclude {dir}", configOverride: config);
        fs.CreateDirectory(dir);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        var fullPath = Path.GetFullPath(dir);
        config.Verify(c => c.SetAsync("sandbox.excludedPaths", fullPath, It.IsAny<CancellationToken>()), Times.Once,
            "新增排除路径应持久化 Path.GetFullPath 归一后的路径");
        output.Should().Contain(fullPath);
    }

    [Fact]
    public async Task Execute_Exclude_AlreadyExcluded_IsIdempotent() {
        var dir = Path.Combine(Path.GetTempPath(), "sb-exclude-dup").ToLowerInvariant();
        var fullPath = Path.GetFullPath(dir);
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("sandbox.excludedPaths", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fullPath);
        var (cmd, _, _, context, fs) = Arrange(arguments: $"exclude {dir}", configOverride: config);
        fs.CreateDirectory(dir);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "路径已在排除列表时不应重复写入");
        output.Should().Contain(fullPath, "重复排除提示应含路径");
    }

    [Fact]
    public async Task Execute_Exclude_WithoutConfigService_DoesNotThrow() {
        var dir = Path.Combine(Path.GetTempPath(), "sb-exclude-noconfig");
        var (cmd, _, config, context, fs) = Arrange(arguments: $"exclude {dir}", withConfig: false);
        fs.CreateDirectory(dir);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("配置服务缺失应优雅降级而非抛异常");
        config.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>生产 bug 修复(TASK065 第五波): 整串 ToLowerInvariant 导致路径被小写化 —
    /// 修复后仅命令词小写化分派,路径不存在提示应保留用户输入的原始大小写。</summary>
    [Fact]
    public async Task Execute_Exclude_MixedCasePath_PreservesOriginalCaseInPrompt() {
        var config = new Mock<IConfigurationService>();
        var (cmd, _, _, context, _) = Arrange(
            arguments: @"exclude C:\No-Such-Dir", configOverride: config);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain(@"C:\No-Such-Dir", "路径不存在提示应保留用户输入的原始大小写");
        output.Should().NotContain(@"c:\no-such-dir", "不应出现整串小写化后的路径");
    }

    /// <summary>生产 bug 修复(TASK065 第五波): 混合大小写目录的排除持久化 —
    /// 写入配置的完整路径应保留用户输入的原始大小写,而非小写化形态。</summary>
    [Fact]
    public async Task Execute_Exclude_MixedCaseDir_PersistsFullPathWithOriginalCase() {
        var dir = Path.Combine(Path.GetTempPath(), "SB-Case-Keep");
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("sandbox.excludedPaths", It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var (cmd, _, _, context, fs) = Arrange(arguments: $"exclude {dir}", configOverride: config);
        fs.CreateDirectory(dir);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        var fullPath = Path.GetFullPath(dir);
        config.Verify(c => c.SetAsync("sandbox.excludedPaths", fullPath, It.IsAny<CancellationToken>()), Times.Once,
            "持久化路径应保留用户输入的原始大小写");
        output.Should().Contain(fullPath, "排除成功提示应含原始大小写完整路径");
    }

    /// <summary>回归保障: 修复仅小写化命令词用于分派 — 大写子命令应照常分派到 exclude 列表分支。</summary>
    [Fact]
    public async Task Execute_UpperCaseSubCommand_StillDispatches() {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.GetAsync("sandbox.excludedPaths", It.IsAny<CancellationToken>()))
            .ReturnsAsync(@"C:\path-a");
        var (cmd, _, _, context, _) = Arrange(arguments: "EXCLUDE", configOverride: config);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain(@"C:\path-a", "大写子命令应照常小写化分派到 exclude");
        output.Should().Contain("/sandbox-toggle exclude <path>");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new SandboxToggleCommand();
        cmd.Name.Should().Be("sandbox-toggle");
        cmd.Usage.Should().Be("/sandbox-toggle [on|off|status|exclude|switch]");
        cmd.Aliases.Should().Contain("sandbox");
        cmd.IsHidden.Should().BeTrue();
    }
}
