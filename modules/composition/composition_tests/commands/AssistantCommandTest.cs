namespace Composition.Tests.Commands;

/// <summary>
/// /assistant 命令单元测试 — 覆盖 on/1 启用(环境变量写入+持久化提示)/off/0 停用/
/// status 状态查询(启用/停用)/未识别参数回退状态/服务缺失静默/元数据一致性
/// <para>AssistantModeService 用 Moq 桩经 DI 注入;环境变量写入后 finally 恢复避免进程污染</para>
/// </summary>
[Collection("command-output")]
public sealed class AssistantCommandTest {
    private const string EnvVarName = "JCC_ASSISTANT_MODE";

    private static (AssistantCommand Command, ChatCommandContext Context) Arrange(
        string arguments = "",
        Mock<IAssistantModeService>? service = null) {
        var services = CommandTestHarness.CreateServices(
            configureDi: di => {
                if (service is not null) di.AddSingleton(service.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new AssistantCommand(), context);
    }

    /// <summary>环境变量作用域 — 写入并在退出时恢复原值,避免污染并行测试进程</summary>
    private static IDisposable EnvScope(string value) {
        var original = Environment.GetEnvironmentVariable(EnvVarName);
        Environment.SetEnvironmentVariable(EnvVarName, value);
        return new EnvRestore(EnvVarName, original);
    }

    private sealed class EnvRestore(string name, string? original) : IDisposable {
        public void Dispose() => Environment.SetEnvironmentVariable(name, original);
    }

    [Theory]
    [InlineData("on")]
    [InlineData("1")]
    public async Task Execute_On_SetsEnvVarAndShowsEnabled(string arguments) {
        using var _ = EnvScope("0");
        var (cmd, context) = Arrange(arguments: arguments);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        Environment.GetEnvironmentVariable(EnvVarName).Should().Be("1",
            $"{arguments} 应写入 {EnvVarName}=1");
        output.Should().Contain("长期助手模式已启用");
        output.Should().Contain($"环境变量 {EnvVarName}=1 已设置", "启用提示应说明环境变量生效范围");
        output.Should().Contain("如需持久化，请在系统环境变量中设置", "应提示持久化方式");
    }

    [Theory]
    [InlineData("off")]
    [InlineData("0")]
    public async Task Execute_Off_ClearsEnvVarAndShowsDisabled(string arguments) {
        using var _ = EnvScope("1");
        var (cmd, context) = Arrange(arguments: arguments);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        Environment.GetEnvironmentVariable(EnvVarName).Should().Be("0",
            $"{arguments} 应写入 {EnvVarName}=0");
        output.Should().Contain("长期助手模式已停用");
    }

    [Fact]
    public async Task Execute_Status_ServiceEnabled_ShowsEnabledState() {
        using var _ = EnvScope("1");
        var service = new Mock<IAssistantModeService>();
        service.Setup(s => s.IsAssistantModeEnabled).Returns(true);
        var (cmd, context) = Arrange(arguments: "status", service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("长期助手模式：");
        output.Should().Contain("状态：已启用");
        output.Should().Contain($"环境变量：{EnvVarName}=1", "状态应回显环境变量当前取值");
    }

    [Fact]
    public async Task Execute_Status_ServiceDisabled_ShowsDisabledState() {
        var service = new Mock<IAssistantModeService>();
        service.Setup(s => s.IsAssistantModeEnabled).Returns(false);
        var (cmd, context) = Arrange(arguments: "status", service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("状态：已停用");
        output.Should().Contain("环境变量：JCC_ASSISTANT_MODE=(未设置)", "环境变量未设置时应显示占位文本");
    }

    [Fact]
    public async Task Execute_EmptyArgs_DefaultsToStatus() {
        var service = new Mock<IAssistantModeService>();
        service.Setup(s => s.IsAssistantModeEnabled).Returns(true);
        var (cmd, context) = Arrange(arguments: "", service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("长期助手模式：", "空参默认等同 status(NullAction=Status)");
    }

    [Fact]
    public async Task Execute_UnrecognizedArg_FallsBackToStatus() {
        var service = new Mock<IAssistantModeService>();
        service.Setup(s => s.IsAssistantModeEnabled).Returns(false);
        var (cmd, context) = Arrange(arguments: "bogus", service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("长期助手模式：", "未识别参数应回退显示状态而非报错");
    }

    [Fact]
    public async Task Execute_WithoutService_StatusPrintsNothingButContinues() {
        var (cmd, context) = Arrange(arguments: "status", service: null);

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("服务缺失时状态打印跳过,命令不应崩溃");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new AssistantCommand();
        cmd.Name.Should().Be("assistant");
        cmd.Usage.Should().Be("/assistant [on|off|status]");
        cmd.Description.Should().Contain("长期助手模式");
        cmd.IsHidden.Should().BeTrue("assistant 为隐藏命令");
        cmd.ArgumentHint.Should().Be("[on|off|status]");
    }
}
