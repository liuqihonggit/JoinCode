namespace Composition.Tests.Commands;

/// <summary>
/// /fast 命令单元测试 — 覆盖 on/off 开关(激活/停用 FastModeService)/enable-disable-1-0 别名/
/// 空参与未识别参数显示状态/FastModeService 缺失时 WorkflowConfig 回退双分支
/// </summary>
[Collection("command-output")]
public sealed class FastCommandTest {
    private static Mock<IFastModeService> CreateFastModeService(bool active = false) {
        var mock = new Mock<IFastModeService>();
        mock.Setup(f => f.IsFastModeActive).Returns(active);
        mock.Setup(f => f.FastModelId).Returns("fast-model");
        mock.Setup(f => f.PrimaryModelId).Returns("primary-model");
        return mock;
    }

    private static (FastCommand Command, ChatCommandContext Context) Arrange(
        string arguments,
        Mock<IFastModeService>? fastMode = null,
        WorkflowConfig? workflowConfig = null) {
        var services = CommandTestHarness.CreateServices(
            workflowConfig: workflowConfig,
            configureDi: di => {
                if (fastMode is not null) di.AddSingleton(fastMode.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new FastCommand(), context);
    }

    [Fact]
    public async Task Execute_On_ActivatesFastModeService() {
        var fastMode = CreateFastModeService();
        var (cmd, context) = Arrange("on", fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        fastMode.Verify(f => f.Activate(), Times.Once, "on 应激活 FastModeService");
        fastMode.Verify(f => f.Deactivate(), Times.Never);
        output.Should().Contain("快速模式: 已启用 (使用 fast-model)");
    }

    [Fact]
    public async Task Execute_Off_DeactivatesFastModeService() {
        var fastMode = CreateFastModeService(active: true);
        var (cmd, context) = Arrange("off", fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        fastMode.Verify(f => f.Deactivate(), Times.Once, "off 应停用 FastModeService");
        fastMode.Verify(f => f.Activate(), Times.Never);
        output.Should().Contain("快速模式: 已禁用 (使用 primary-model)");
    }

    [Theory]
    [InlineData("enable")]
    [InlineData("1")]
    public async Task Execute_OnAliases_ActivateFastMode(string arguments) {
        var fastMode = CreateFastModeService();
        var (cmd, context) = Arrange(arguments, fastMode: fastMode);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        fastMode.Verify(f => f.Activate(), Times.Once, $"{arguments} 应作为 on 的别名激活快速模式");
    }

    [Theory]
    [InlineData("disable")]
    [InlineData("0")]
    public async Task Execute_OffAliases_DeactivateFastMode(string arguments) {
        var fastMode = CreateFastModeService();
        var (cmd, context) = Arrange(arguments, fastMode: fastMode);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        fastMode.Verify(f => f.Deactivate(), Times.Once, $"{arguments} 应作为 off 的别名停用快速模式");
    }

    [Fact]
    public async Task Execute_EmptyArgs_ShowsStatusWithoutToggling() {
        var fastMode = CreateFastModeService(active: false);
        var (cmd, context) = Arrange("", fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        fastMode.Verify(f => f.Activate(), Times.Never, "无参数仅显示状态,不应切换");
        fastMode.Verify(f => f.Deactivate(), Times.Never);
        output.Should().Contain("快速模式: 已禁用 (当前模型: primary-model)");
        output.Should().Contain("使用 /fast on 启用，/fast off 禁用");
    }

    [Fact]
    public async Task Execute_ActiveServiceStatus_ShowsFastModel() {
        var fastMode = CreateFastModeService(active: true);
        var (cmd, context) = Arrange("", fastMode: fastMode);

        var (_, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        output.Should().Contain("快速模式: 已启用 (当前模型: fast-model)",
            "激活状态显示当前使用的是快速模型");
    }

    [Fact]
    public async Task Execute_UnrecognizedArgument_ShowsStatusOnly() {
        var fastMode = CreateFastModeService();
        var (cmd, context) = Arrange("bogus", fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        fastMode.Verify(f => f.Activate(), Times.Never, "未识别参数不应触发任何切换");
        fastMode.Verify(f => f.Deactivate(), Times.Never);
        output.Should().Contain("使用 /fast on 启用，/fast off 禁用", "未识别参数应回退显示状态与用法提示");
    }

    [Fact]
    public async Task Execute_On_WithoutService_SetsWorkflowConfigFastMode() {
        var config = new WorkflowConfig();
        var (cmd, context) = Arrange("on", fastMode: null, workflowConfig: config);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        // with 表达式创建新 record 赋回 CommandServices,须从服务包读取替换后的配置
        var effectiveConfig = context.GetCommandServices().WorkflowConfig;
        effectiveConfig.Should().NotBeNull();
        Assert.NotNull(effectiveConfig);
        effectiveConfig.FastMode.Should().BeTrue("FastModeService 缺失时应回退设置 WorkflowConfig.FastMode 标志");
        output.Should().Contain("快速模式: 已启用 (使用 fast model)",
            "无服务时模型名回退为占位文案 fast model");
    }

    [Fact]
    public async Task Execute_Off_WithoutService_ClearsWorkflowConfigFastMode() {
        var config = new WorkflowConfig { FastMode = true };
        var (cmd, context) = Arrange("off", fastMode: null, workflowConfig: config);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        var effectiveConfig = context.GetCommandServices().WorkflowConfig;
        effectiveConfig.Should().NotBeNull();
        Assert.NotNull(effectiveConfig);
        effectiveConfig.FastMode.Should().BeFalse("off 在服务缺失时应清除 WorkflowConfig.FastMode 标志");
        output.Should().Contain("快速模式: 已禁用 (使用 primary model)");
    }

    [Fact]
    public async Task Execute_On_NeitherServiceNorConfig_ShowsPlaceholderModel() {
        var (cmd, context) = Arrange("on", fastMode: null, workflowConfig: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("快速模式: 已启用 (使用 fast model)",
            "服务与配置均缺失时不应抛异常,回退占位模型名");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new FastCommand();
        cmd.Name.Should().Be("fast");
        cmd.Usage.Should().Be("/fast [on|off]");
        cmd.Description.Should().Contain("快速模式");
    }
}
