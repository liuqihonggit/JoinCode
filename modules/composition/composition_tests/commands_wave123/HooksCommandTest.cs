namespace Composition.Tests.Commands;

/// <summary>
/// /hooks 命令单元测试 — 覆盖 list(管理器缺失/空配置/已有 Hook 纯文本渲染)/add(缺参用法/未知事件/成功添加)/
/// remove(两级缺参用法/未知事件/成功移除)/test(缺参/未知事件/有效事件提示)/未知操作
/// <para>IHookConfigurationManager 经本地构造 CommandServices 注入,用 Moq 模拟</para>
/// </summary>
[Collection("command-output")]
public sealed class HooksCommandTest {
    private static (HooksCommand Command, Mock<IHookConfigurationManager> Manager, ChatCommandContext Context) Arrange(
        string arguments = "",
        HookConfigurationGroup? group = null) {
        var manager = new Mock<IHookConfigurationManager>();
        manager.Setup(m => m.LoadAllHooksAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(group ?? new HookConfigurationGroup());
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
            HookConfigurationManager = manager.Object,
        };
        var context = CommandTestHarness.CreateContext(commandServices, arguments: arguments);
        return (new HooksCommand(), manager, context);
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

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => new HooksCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        result.IsHandled.Should().BeTrue();
        output.Should().Contain("Hook 配置管理器未初始化");
    }

    [Fact]
    public async Task Execute_ListEmptyConfig_ShowsEmptyHint() {
        var (cmd, _, context) = Arrange(arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("Hook 配置列表:");
        output.Should().Contain("当前无已配置的 Hook");
        output.Should().Contain("使用 /hooks add <event> <command> 添加 Hook");
        output.Should().Contain("支持的事件: PreToolUse, PostToolUse, Notification, Stop, SessionStart");
    }

    [Fact]
    public async Task Execute_ListWithHooks_RendersNonInteractiveEntries() {
        var group = new HookConfigurationGroup();
        group.Add(new SourcedHookConfig {
            Event = HookEvent.PreToolUse,
            Command = new BashCommandHook { Command = "echo 检查权限" },
            Matcher = null,
            Source = HookSource.UserSettings,
        });
        group.Add(new SourcedHookConfig {
            Event = HookEvent.Stop,
            Command = new BashCommandHook { Command = "notify-send done" },
            Matcher = "Bash(git *)",
            Source = HookSource.ProjectSettings,
        });
        var (cmd, _, context) = Arrange(arguments: "list", group: group);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("[UserSettings] PreToolUse matcher=* → echo 检查权限", "Matcher 为 null 时应显示 *");
        output.Should().Contain("[ProjectSettings] Stop matcher=Bash(git *) → notify-send done");
    }

    [Fact]
    public async Task Execute_AddWithoutEnoughArgs_ShowsUsageWithExample() {
        var (cmd, manager, context) = Arrange(arguments: "add PreToolUse");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /hooks add <event> <command>");
        output.Should().Contain("示例: /hooks add PreToolUse 'echo 检查权限'");
        manager.Verify(m => m.AddHookAsync(It.IsAny<HookSource>(), It.IsAny<HookEvent>(), It.IsAny<string>(), It.IsAny<HookCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_AddWithUnknownEvent_ShowsSupportedEvents() {
        var (cmd, _, context) = Arrange(arguments: "add bogus echo hi");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("未知事件: bogus");
        output.Should().Contain("PreToolUse, PostToolUse, Notification, Stop, SessionStart, SubagentStart, SubagentStop, PreCompact, PostCompact");
    }

    [Fact]
    public async Task Execute_AddValidHook_PersistsUserSettingsHook() {
        var (cmd, manager, context) = Arrange(arguments: "add PreToolUse echo 检查权限");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已添加 Hook: 事件=PreToolUse, 命令=echo 检查权限");
        manager.Verify(m => m.AddHookAsync(
            HookSource.UserSettings,
            HookEvent.PreToolUse,
            null,
            It.Is<BashCommandHook>(h => h.Command == "echo 检查权限"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_RemoveWithoutEnoughArgs_ShowsUsage() {
        var (cmd, _, context) = Arrange(arguments: "remove");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /hooks remove <event> <command>");
    }

    [Fact]
    public async Task Execute_RemoveEventOnly_ShowsUsage() {
        var (cmd, _, context) = Arrange(arguments: "remove PreToolUse");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("用法: /hooks remove <event> <command>", "remove 只有事件缺命令时应提示用法");
    }

    [Fact]
    public async Task Execute_RemoveValidHook_PersistsRemoval() {
        var (cmd, manager, context) = Arrange(arguments: "remove PreToolUse echo 检查权限");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已移除 Hook: 事件=PreToolUse, 命令=echo 检查权限");
        manager.Verify(m => m.RemoveHookAsync(
            HookSource.UserSettings,
            HookEvent.PreToolUse,
            null,
            It.Is<BashCommandHook>(h => h.Command == "echo 检查权限"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_RemoveWithUnknownEvent_ShowsUnknownEvent() {
        var (cmd, manager, context) = Arrange(arguments: "remove bogus echo hi");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("未知事件: bogus");
        manager.Verify(m => m.RemoveHookAsync(It.IsAny<HookSource>(), It.IsAny<HookEvent>(), It.IsAny<string>(), It.IsAny<HookCommand>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Execute_TestWithoutEvent_ShowsUsage() {
        var (cmd, _, context) = Arrange(arguments: "test");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("用法: /hooks test <event> [matcher]");
    }

    [Fact]
    public async Task Execute_TestWithUnknownEvent_ShowsUnknownEvent() {
        var (cmd, _, context) = Arrange(arguments: "test bogus");

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("未知事件: bogus");
    }

    [Fact]
    public async Task Execute_TestWithValidEvent_AnnouncesTestMode() {
        var (cmd, _, context) = Arrange(arguments: "test PreToolUse");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("测试 Hook 事件: PreToolUse");
        output.Should().Contain("Hook 测试功能需要通过实际事件触发来验证");
    }

    [Fact]
    public async Task Execute_UnknownAction_ShowsSupportedActions() {
        var (cmd, _, context) = Arrange(arguments: "bogus");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知操作: bogus");
        output.Should().Contain("支持的操作: list, add, remove, test");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new HooksCommand();
        cmd.Name.Should().Be("hooks");
        cmd.Usage.Should().StartWith("/hooks");
        cmd.Aliases.Should().BeEmpty();
    }
}
