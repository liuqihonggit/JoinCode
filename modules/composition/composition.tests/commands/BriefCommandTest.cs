namespace Composition.Tests.Commands;

/// <summary>
/// /brief 命令单元测试 — 覆盖 on/off/toggle 三分支(BriefModeService 状态迁移+状态打印)/
/// 权限拦截(无 entitlement 不启用)/服务缺失静默/状态变化注入 SystemReminder/
/// IsEnabled 环境变量覆盖语义/元数据一致性
/// <para>BriefModeService 用 Moq 桩(闭包状态模拟 Enable/Disable/Toggle 副作用)经
/// CommandServices.BriefModeService 注入;EntitlementService 经 DI 注入;
/// SystemReminderManager 用真实实例(AddReminderAsync 非 virtual 无法 Moq)验证提醒副作用</para>
/// </summary>
[Collection("command-output")]
public sealed class BriefCommandTest {
    private const string EnvVarName = "JCC_BRIEF";

    /// <summary>带闭包状态的 BriefModeService 桩 — Enable/Disable/Toggle 真实迁移 IsEnabled</summary>
    private sealed class StatefulBriefService {
        public bool Enabled;
        public DateTime? EnabledAt;

        public Mock<IBriefModeService> ToMock() {
            var mock = new Mock<IBriefModeService>();
            mock.Setup(s => s.IsEnabled).Returns(() => Enabled);
            mock.Setup(s => s.EnabledAt).Returns(() => EnabledAt);
            mock.Setup(s => s.Enable()).Callback(() => { Enabled = true; EnabledAt = DateTime.UtcNow; });
            mock.Setup(s => s.Disable()).Callback(() => Enabled = false);
            mock.Setup(s => s.Toggle()).Callback(() => {
                Enabled = !Enabled;
                EnabledAt = Enabled ? DateTime.UtcNow : null;
            }).Returns(() => Enabled);
            return mock;
        }
    }

    private static (BriefCommand Command, ChatCommandContext Context, StatefulBriefService State) Arrange(
        string arguments = "",
        IBriefModeService? briefService = null,
        IEntitlementService? entitlement = null,
        SystemReminderManager? reminderManager = null) {
        var state = new StatefulBriefService();
        var di = new ServiceCollection();
        if (entitlement is not null) di.AddSingleton(entitlement);
        if (reminderManager is not null) di.AddSingleton(reminderManager);
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = di.BuildServiceProvider(),
            BriefModeService = briefService ?? state.ToMock().Object,
        };
        var context = new ChatCommandContext {
            Arguments = arguments,
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };
        return (new BriefCommand(), context, state);
    }

    private static IEntitlementService Entitlement(bool briefEntitled) {
        var mock = new Mock<IEntitlementService>();
        mock.Setup(e => e.IsBriefEntitled).Returns(briefEntitled);
        return mock.Object;
    }

    [Fact]
    public async Task Execute_On_EnablesBriefModeAndPrintsStatus() {
        var (cmd, context, state) = Arrange(arguments: "on");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        state.Enabled.Should().BeTrue("on 应调用 Enable 启用简要模式");
        output.Should().Contain("简要消息模式已启用");
        output.Should().Contain("LLM 将通过 send_user_message 工具回复用户",
            "启用状态应说明回复通道切换");
    }

    [Fact]
    public async Task Execute_On_EnabledWithTimestamp_ShowsEnabledAt() {
        var fixedAt = new DateTime(2026, 1, 15, 10, 30, 0);
        var mock = new Mock<IBriefModeService>();
        mock.Setup(s => s.IsEnabled).Returns(true);
        mock.Setup(s => s.EnabledAt).Returns(fixedAt);
        var (cmd, context, _) = Arrange(arguments: "on", briefService: mock.Object);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("启用时间: 2026-01-15 10:30:00", "已启用且有时间时应渲染启用时间");
    }

    [Fact]
    public async Task Execute_On_WithoutEntitlement_BlockedWithMutedHint() {
        var mock = new Mock<IBriefModeService>();
        mock.Setup(s => s.IsEnabled).Returns(false);
        var (cmd, context, _) = Arrange(
            arguments: "on",
            briefService: mock.Object,
            entitlement: Entitlement(briefEntitled: false));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("简要模式未启用 — 当前账户无权限", "无权限时应拦截并说明原因");
        mock.Verify(s => s.Enable(), Times.Never, "无权限时不应启用");
    }

    [Fact]
    public async Task Execute_Off_DisablesBriefModeAndPrintsStatus() {
        var (cmd, context, state) = Arrange(arguments: "off");
        state.Enabled = true;
        state.EnabledAt = DateTime.UtcNow;

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        state.Enabled.Should().BeFalse("off 应调用 Disable 停用简要模式");
        output.Should().Contain("简要消息模式已禁用 - LLM 将使用普通文本回复");
    }

    [Fact]
    public async Task Execute_EmptyArgs_TogglesBriefMode() {
        var (cmd, context, state) = Arrange(arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        state.Enabled.Should().BeTrue("空参走 Toggle 分支(NullAction 默认 Toggle),false→true");
        output.Should().Contain("简要消息模式已启用");
    }

    [Fact]
    public async Task Execute_EmptyArgs_DisabledWithoutEntitlement_Blocked() {
        var mock = new Mock<IBriefModeService>();
        mock.Setup(s => s.IsEnabled).Returns(false);
        var (cmd, context, _) = Arrange(
            arguments: "",
            briefService: mock.Object,
            entitlement: Entitlement(briefEntitled: false));

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("简要模式未启用 — 当前账户无权限", "toggle 自关闭态开启同样受权限门控");
        mock.Verify(s => s.Toggle(), Times.Never, "无权限时 toggle 不应执行");
    }

    [Fact]
    public async Task Execute_StateChange_InjectsBriefStateReminder() {
        await using var reminderManager = new SystemReminderManager();
        var (cmd, context, _) = Arrange(arguments: "on", reminderManager: reminderManager);

        await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        var reminders = await reminderManager.GetRemindersAsync();
        reminders.Should().ContainSingle("状态变化应注入一条 brief-mode-state 提醒");
        var reminder = reminders[0];
        reminder.Id.Should().Be("brief-mode-state");
        reminder.Content.Should().Contain("Brief mode is now enabled");
        reminder.Content.Should().Contain("send_user_message", "提醒应告知 LLM 使用 SendUserMessage 工具");
        reminder.Priority.Should().Be(10);
    }

    [Fact]
    public async Task Execute_Disable_InjectsDisabledReminder() {
        await using var reminderManager = new SystemReminderManager();
        var (cmd, context, state) = Arrange(arguments: "off", reminderManager: reminderManager);
        state.Enabled = true;

        await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        var reminders = await reminderManager.GetRemindersAsync();
        reminders.Should().ContainSingle("停用状态变化应注入提醒");
        reminders[0].Content.Should().Contain("Brief mode is now disabled");
        reminders[0].Content.Should().Contain("no longer available");
    }

    [Fact]
    public async Task Execute_NoStateChange_NoReminderInjected() {
        await using var reminderManager = new SystemReminderManager();
        var mock = new Mock<IBriefModeService>();
        mock.Setup(s => s.IsEnabled).Returns(true); // 已启用,on 无状态变化
        mock.Setup(s => s.Enable()).Callback(() => { });
        var (cmd, context, _) = Arrange(arguments: "on", briefService: mock.Object, reminderManager: reminderManager);

        await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        var reminders = await reminderManager.GetRemindersAsync();
        reminders.Should().BeEmpty("状态未变化时不应重复注入提醒");
    }

    [Fact]
    public async Task Execute_WithoutBriefService_ReturnsSilently() {
        var commandServices = new CommandServices {
            ChatService = Mock.Of<IChatService>(),
            CodeService = Mock.Of<ICodeService>(),
            PlanService = Mock.Of<IPlanService>(),
            FileSystem = TestFileSystem.Current,
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
        };
        var context = new ChatCommandContext {
            Arguments = "on",
            SessionId = "test-session",
            CancellationToken = CancellationToken.None,
            Services = new CommandServiceProvider(commandServices),
        };

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new BriefCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("服务缺失应静默返回而非崩溃");
        output.Should().NotContain("简要消息模式已启用");
    }

    [Theory]
    [InlineData("0", false)]
    [InlineData("false", false)]
    [InlineData("FALSE", false)]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("", true)]
    public void IsEnabled_EnvVarOverridesDefaultState(string envValue, bool expected) {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        try {
            Environment.SetEnvironmentVariable(EnvVarName, envValue.Length == 0 ? null : envValue);
            new BriefCommand().IsEnabled.Should().Be(expected,
                $"环境变量 {EnvVarName}='{envValue}' 应得到 IsEnabled={expected}(非 0/false 即启用,未设置默认启用)");
        } finally {
            Environment.SetEnvironmentVariable(EnvVarName, original);
        }
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new BriefCommand();
        cmd.Name.Should().Be("brief");
        cmd.Usage.Should().Be("/brief [on|off]");
        cmd.Description.Should().Contain("简要消息模式");
    }
}
