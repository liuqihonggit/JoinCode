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
        bool isInSandbox = false,
        WorkflowConfig? workflowConfig = null) {
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
                if (workflowConfig is not null) di.AddSingleton(workflowConfig);
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
    public async Task Execute_Exclude_WithoutPattern_ShowsConfiguredCommands() {
        var cfg = new WorkflowConfig { Sandbox = new SandboxConfig { ExcludedCommands = ["docker:*", "psql"] } };
        var (cmd, _, _, context, _) = Arrange(arguments: "exclude", workflowConfig: cfg);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("docker:*", "当前排除命令清单应渲染");
        output.Should().Contain("psql");
        output.Should().Contain("sandbox.excludedCommands", "应指明配置字段");
    }

    [Fact]
    public async Task Execute_Exclude_EmptyList_ShowsNone() {
        var cfg = new WorkflowConfig { Sandbox = new SandboxConfig() };
        var (cmd, _, _, context, _) = Arrange(arguments: "exclude", workflowConfig: cfg);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("sandbox.excludedCommands");
    }

    [Fact]
    public async Task Execute_Exclude_WithPattern_OnlyGuidesSettingsEdit() {
        var configMock = new Mock<IConfigurationService>();
        var (cmd, _, _, context, _) = Arrange(arguments: "exclude docker:*", configOverride: configMock);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("docker:*", "指引应回显用户输入模式");
        output.Should().Contain("settings.json");
        configMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "配置服务不支持数组写入，命令不得再写已删除的 sandbox.excludedPaths");
    }

    [Fact]
    public async Task Execute_Exclude_UpperCaseSubCommand_StillDispatches() {
        var cfg = new WorkflowConfig { Sandbox = new SandboxConfig { ExcludedCommands = ["docker:*"] } };
        var (cmd, _, _, context, _) = Arrange(arguments: "EXCLUDE", workflowConfig: cfg);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("docker:*", "大写子命令应照常小写化分派到 exclude");
    }

    [Fact]
    public async Task Execute_EmptyArgs_ShowsSettingsMenu() {
        var (cmd, _, _, context, _) = Arrange();

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("沙箱设置（settings.json current.sandbox）");
        output.Should().Contain("enabled=");
        output.Should().Contain("autoAllowBashIfSandboxed=");
        output.Should().Contain("/sandbox-toggle help");
    }

    [Fact]
    public async Task Execute_Help_ShowsAllSubcommands() {
        var (cmd, _, _, context, _) = Arrange(arguments: "help");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("status");
        output.Should().Contain("on|enable");
        output.Should().Contain("off|disable");
        output.Should().Contain("exclude");
        output.Should().Contain("switch <type>");
    }

    [Fact]
    public async Task Execute_OnHelp_ShowsHelpAndDoesNotEnter() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "on --help");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /sandbox-toggle <子命令>");
        manager.Verify(m => m.EnterSandboxAsync(It.IsAny<SandboxOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_UnknownSubcommand_ShowsErrorAndHelp() {
        var (cmd, manager, _, context, _) = Arrange(arguments: "bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知沙箱子命令: bogus");
        output.Should().Contain("用法: /sandbox-toggle <子命令>");
        manager.Verify(m => m.EnterSandboxAsync(It.IsAny<SandboxOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_On_PolicyLocked_DoesNotEnterOrWrite() {
        var cfg = new WorkflowConfig {
            SandboxPolicy = new SandboxPolicyConfig {
                Locked = true,
                LockedKeys = [SandboxPolicyResolver.KeyEnabled, SandboxPolicyResolver.KeyAutoAllow],
            },
        };
        var configMock = new Mock<IConfigurationService>();
        var (cmd, manager, _, context, _) = Arrange(arguments: "on", workflowConfig: cfg, configOverride: configMock);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("企业策略锁定");
        output.Should().Contain(SandboxPolicyResolver.KeyEnabled);
        manager.Verify(m => m.EnterSandboxAsync(It.IsAny<SandboxOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        configMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_On_FlagLocked_ReportsSettingsSource() {
        var cfg = new WorkflowConfig {
            SandboxPolicy = new SandboxPolicyConfig {
                Locked = true,
                LockedKeys = [SandboxPolicyResolver.KeyEnabled],
                FlagLockedKeys = [SandboxPolicyResolver.KeyEnabled],
            },
        };
        var configMock = new Mock<IConfigurationService>();
        var (cmd, manager, _, context, _) = Arrange(arguments: "on", workflowConfig: cfg, configOverride: configMock);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("被 --settings 锁定");
        output.Should().Contain("--settings 已定义 sandbox.enabled");
        output.Should().Contain("修改该文件/内联 JSON");
        manager.Verify(m => m.EnterSandboxAsync(It.IsAny<SandboxOptions>(), It.IsAny<CancellationToken>()), Times.Never);
        configMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_Off_PolicyLocked_DoesNotExitOrWrite() {
        var cfg = new WorkflowConfig {
            SandboxPolicy = new SandboxPolicyConfig { Locked = true, LockedKeys = [SandboxPolicyResolver.KeyAllowUnsandboxed] },
        };
        var configMock = new Mock<IConfigurationService>();
        var (cmd, manager, _, context, _) = Arrange(arguments: "off", workflowConfig: cfg, configOverride: configMock);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("企业策略锁定");
        manager.Verify(m => m.ExitSandboxAsync(It.IsAny<CancellationToken>()), Times.Never);
        configMock.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_SwitchWithType_PolicyLocked_DoesNotSwitch() {
        var cfg = new WorkflowConfig {
            SandboxPolicy = new SandboxPolicyConfig { Locked = true, LockedKeys = [SandboxPolicyResolver.KeyEnabled] },
        };
        var (cmd, manager, _, context, _) = Arrange(arguments: "switch docker", workflowConfig: cfg);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("企业策略锁定");
        manager.Verify(m => m.SwitchProviderAsync(It.IsAny<SandboxType>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PersistSandboxEnabled_WritesRegisteredSettingsKey() {
        var config = new Mock<IConfigurationService>();
        config.Setup(c => c.SetAsync(ConfigKeys.SandboxEnabled, "true", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        var (_, _, _, context, _) = Arrange(configOverride: config);

        var written = await SandboxToggleCommand.PersistSandboxEnabledAsync(context, enabled: true);

        written.Should().BeTrue();
        config.Verify(c => c.SetAsync(ConfigKeys.SandboxEnabled, "true", It.IsAny<CancellationToken>()), Times.Once,
            "on/off 必须把 sandbox.enabled 写进 settings，而不是只改会话状态");
    }

    [Fact]
    public async Task PersistSandboxEnabled_WithoutConfigService_ReturnsFalse() {
        var (_, _, _, context, _) = Arrange(withConfig: false);

        var written = await SandboxToggleCommand.PersistSandboxEnabledAsync(context, enabled: false);

        written.Should().BeFalse("配置服务缺失时应返回 false 而不是抛异常");
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
