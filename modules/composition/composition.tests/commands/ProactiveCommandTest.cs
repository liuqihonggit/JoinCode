namespace Composition.Tests.Commands;

/// <summary>
/// /proactive 命令单元测试 — 覆盖 on(含别名)激活/off(含别名)停用/pause/resume(含短别名)/
/// status 状态查询/未识别参数引导/服务缺失静默/元数据一致性
/// <para>ProactiveStateService 用 Moq 桩经 DI 注入</para>
/// </summary>
[Collection("command-output")]
public sealed class ProactiveCommandTest {
    private static Mock<IProactiveStateService> Service(
        bool active = false,
        bool paused = false,
        bool contextBlocked = false) {
        var mock = new Mock<IProactiveStateService>();
        mock.Setup(s => s.IsActive).Returns(active);
        mock.Setup(s => s.IsPaused).Returns(paused);
        mock.Setup(s => s.IsContextBlocked).Returns(contextBlocked);
        return mock;
    }

    private static (ProactiveCommand Command, ChatCommandContext Context, Mock<IProactiveStateService> Service) Arrange(
        string arguments = "",
        Mock<IProactiveStateService>? service = null) {
        var proactiveService = service ?? Service();
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(proactiveService.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new ProactiveCommand(), context, proactiveService);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("activate")]
    [InlineData("1")]
    public async Task Execute_On_ActivatesProactiveMode(string arguments) {
        var service = Service();
        var (cmd, context, _) = Arrange(arguments: arguments, service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        service.Verify(s => s.Activate("user-command"), Times.Once,
            $"{arguments} 应以 user-command 来源激活主动模式");
        output.Should().Contain("主动模式已激活");
        output.Should().Contain("主动执行模式:", "执行尾部应回显状态");
    }

    [Theory]
    [InlineData("off")]
    [InlineData("deactivate")]
    [InlineData("0")]
    public async Task Execute_Off_DeactivatesProactiveMode(string arguments) {
        var service = Service(active: true);
        var (cmd, context, _) = Arrange(arguments: arguments, service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        service.Verify(s => s.Deactivate(), Times.Once, $"{arguments} 应停用主动模式");
        output.Should().Contain("主动模式已停用");
    }

    [Theory]
    [InlineData("pause")]
    [InlineData("p")]
    public async Task Execute_Pause_PausesProactiveMode(string arguments) {
        var service = Service(active: true);
        var (cmd, context, _) = Arrange(arguments: arguments, service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        service.Verify(s => s.Pause(), Times.Once, $"{arguments} 应暂停主动模式");
        service.Verify(s => s.Activate(It.IsAny<string?>()), Times.Never, "pause 不应触发激活");
        output.Should().Contain("主动模式已暂停");
    }

    [Theory]
    [InlineData("resume")]
    [InlineData("r")]
    public async Task Execute_Resume_ResumesProactiveMode(string arguments) {
        var service = Service(active: true, paused: true);
        var (cmd, context, _) = Arrange(arguments: arguments, service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        service.Verify(s => s.Resume(), Times.Once, $"{arguments} 应恢复主动模式");
        output.Should().Contain("主动模式已恢复");
    }

    [Theory]
    [InlineData("status")]
    [InlineData("s")]
    [InlineData("")]
    public async Task Execute_Status_RendersFullState(string arguments) {
        var service = Service(active: true, paused: true, contextBlocked: false);
        var (cmd, context, _) = Arrange(arguments: arguments, service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("主动执行模式:");
        output.Should().Contain("  激活: 是");
        output.Should().Contain("  暂停: 是");
        output.Should().Contain("  上下文阻塞: 否", "状态应覆盖激活/暂停/上下文阻塞三维度");
        service.Verify(s => s.Activate(It.IsAny<string?>()), Times.Never);
        service.Verify(s => s.Deactivate(), Times.Never);
        service.Verify(s => s.Pause(), Times.Never, "status 仅查询不修改状态");
    }

    [Fact]
    public async Task Execute_UnrecognizedArg_ShowsErrorAndUsage() {
        var service = Service();
        var (cmd, context, _) = Arrange(arguments: "bogus", service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知参数: bogus");
        output.Should().Contain("用法: /proactive [on|off|pause|resume|status]", "未识别参数应引导正确用法");
        service.Verify(s => s.Activate(It.IsAny<string?>()), Times.Never, "未识别参数不应触发任何状态变更");
    }

    /// <summary>生产 bug 登记(TASK065 brain 域): IProactiveStateService 缺失时 OnEnabledAsync 的
    /// "主动模式已激活" 提示在 proactiveService?.Activate() 安全调用之后仍无条件打印 —
    /// 服务未注册却向用户宣称激活成功,输出与实际状态不符。本测试按当前实现固化行为
    /// (提示输出 + Activate 未调用);修复后应改为断言提示不输出或改为服务不可用提示。</summary>
    [Fact]
    public async Task Execute_WithoutService_ContinuesWithMisleadingActivationNotice() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "on");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new ProactiveCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("服务缺失时应静默降级(proactiveService?. 安全访问)而非崩溃");
        output.Should().Contain("主动模式已激活",
            "当前实现: 服务缺失时激活提示仍输出(bug 已登记,未擅自修复)");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ProactiveCommand();
        cmd.Name.Should().Be("proactive");
        cmd.Usage.Should().Be("/proactive [on|off|pause|resume|status]");
        cmd.Description.Should().Contain("主动执行模式");
        cmd.IsHidden.Should().BeTrue("proactive 为隐藏命令");
        cmd.ArgumentHint.Should().Be("[on|off|pause|resume|status]");
    }
}
