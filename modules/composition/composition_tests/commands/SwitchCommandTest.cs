namespace Composition.Tests.Commands;

/// <summary>
/// /switch 命令单元测试 — 覆盖服务缺失降级/空参显示当前模式(全部/指定/活跃代理列表标记)/
/// all 切回全部/按名称切换(找到/未找到)/AgentService 缺失降级/元数据一致性
/// <para>IAgentOutputChannelManager 与 IAgentService 用 Moq 桩经 DI 注入</para>
/// </summary>
[Collection("command-output")]
public sealed class SwitchCommandTest {
    private static (SwitchCommand Command, ChatCommandContext Context, Mock<IAgentOutputChannelManager> OutputManager, Mock<IAgentService>? AgentService) Arrange(
        string arguments = "",
        Mock<IAgentOutputChannelManager>? outputManager = null,
        Mock<IAgentService>? agentService = null) {
        var manager = outputManager ?? new Mock<IAgentOutputChannelManager>();
        var services = CommandTestHarness.CreateServices(
            configureDi: di => {
                di.AddSingleton(manager.Object);
                if (agentService is not null) di.AddSingleton(agentService.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new SwitchCommand(), context, manager, agentService);
    }

    private static Mock<IAgentOutputChannelManager> ManagerWithAgents(
        string? displayMode = null,
        params AgentOutputInfo[] agents) {
        var manager = new Mock<IAgentOutputChannelManager>();
        manager.Setup(m => m.GetDisplayMode()).Returns(displayMode);
        manager.Setup(m => m.GetActiveAgents()).Returns(agents);
        return manager;
    }

    [Fact]
    public async Task Execute_WithoutOutputManager_ShowsError() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new SwitchCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("IAgentOutputChannelManager 服务未初始化", "输出管理器缺失应提示而非崩溃");
    }

    [Fact]
    public async Task Execute_EmptyArgs_ShowAllModeWithoutAgents() {
        var manager = ManagerWithAgents(displayMode: null);
        var (cmd, context, _, _) = Arrange(outputManager: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前模式: 显示全部子代理输出");
        output.Should().Contain("当前无活跃子代理", "无活跃代理时应提示空列表");
        manager.Verify(m => m.SetDisplayMode(It.IsAny<string?>()), Times.Never, "查看模式不应修改显示模式");
    }

    [Fact]
    public async Task Execute_EmptyArgs_FocusedModeMarksCurrentAgent() {
        var manager = ManagerWithAgents(
            displayMode: "agent-1",
            new AgentOutputInfo { AgentId = "agent-1", DisplayName = "explore" },
            new AgentOutputInfo { AgentId = "agent-2", DisplayName = "coder" });
        var (cmd, context, _, _) = Arrange(outputManager: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前模式: 只看子代理 agent-1 的输出");
        output.Should().Contain("活跃子代理:");
        output.Should().Contain("  explore *", "当前选中代理应带 * 标记");
        output.Should().Contain("  coder");
        output.Should().Contain("  (* = 当前选中)", "列表应附图例说明");
    }

    [Fact]
    public async Task Execute_EmptyArgs_AgentWithoutDisplayName_FallsBackToId() {
        var manager = ManagerWithAgents(
            displayMode: null,
            new AgentOutputInfo { AgentId = "agent-9", DisplayName = null });
        var (cmd, context, _, _) = Arrange(outputManager: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("  agent-9", "无显示名时应回退渲染 AgentId");
    }

    [Theory]
    [InlineData("all")]
    [InlineData("ALL")]
    public async Task Execute_All_ResetsDisplayModeToNull(string arguments) {
        var manager = new Mock<IAgentOutputChannelManager>();
        var (cmd, context, _, _) = Arrange(arguments: arguments, outputManager: manager);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.SetDisplayMode(null), Times.Once, "all 应重置显示模式为全部(大小写不敏感)");
        output.Should().Contain("已切换到显示全部子代理输出");
    }

    [Fact]
    public async Task Execute_AgentNameFound_SwitchesToAgentOutput() {
        var manager = new Mock<IAgentOutputChannelManager>();
        var agentService = new Mock<IAgentService>();
        agentService.Setup(a => a.FindAgentIdByNameAsync("explore", It.IsAny<CancellationToken>()))
            .ReturnsAsync("agent-1");
        var (cmd, context, _, _) = Arrange(arguments: "explore", outputManager: manager, agentService: agentService);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        manager.Verify(m => m.SetDisplayMode("agent-1"), Times.Once, "按名称切换应写入解析到的 AgentId");
        output.Should().Contain("已切换到只看子代理 explore 的输出");
    }

    [Fact]
    public async Task Execute_AgentNameNotFound_ShowsWarningAndCurrentMode() {
        var manager = ManagerWithAgents(displayMode: null);
        var agentService = new Mock<IAgentService>();
        agentService.Setup(a => a.FindAgentIdByNameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);
        var (cmd, context, _, _) = Arrange(arguments: "ghost", outputManager: manager, agentService: agentService);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未找到子代理 ghost", "未找到代理应给出明确警告");
        output.Should().Contain("当前模式: 显示全部子代理输出", "未找到时应回显当前模式辅助定位");
        manager.Verify(m => m.SetDisplayMode(It.IsAny<string?>()), Times.Never, "未找到代理不应改变显示模式");
    }

    [Fact]
    public async Task Execute_WithoutAgentService_ShowsError() {
        var manager = new Mock<IAgentOutputChannelManager>();
        var (cmd, context, _, _) = Arrange(arguments: "explore", outputManager: manager, agentService: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("IAgentService 服务未初始化", "代理服务缺失应提示而非崩溃");
        manager.Verify(m => m.SetDisplayMode(It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new SwitchCommand();
        cmd.Name.Should().Be("switch");
        cmd.Usage.Should().Be("/switch [agentName|all]");
        cmd.Description.Should().Contain("切换查看指定子代理输出");
        cmd.ArgumentHint.Should().Be("[agentName|all]");
    }
}
