namespace Composition.Tests.Commands;

/// <summary>
/// /sampling 命令单元测试 — 覆盖提供者缺失降级/无参查询(有值+默认值)/unset 重置/
/// 温度+最大Token 更新/仅温度(保持最大)/无效温度(非数字/超范围)/无效最大Token
/// <para>IExecutionSettingsProvider 用 Moq SetupProperty 桩经 harness 注入,断言写入的实际值</para>
/// </summary>
[Collection("command-output")]
public sealed class SamplingCommandTest {
    private static Mock<IExecutionSettingsProvider> CreateProvider(float? temperature = null, int? maxTokens = null) {
        var provider = new Mock<IExecutionSettingsProvider>();
        provider.SetupProperty(p => p.Temperature, temperature);
        provider.SetupProperty(p => p.MaxTokens, maxTokens);
        return provider;
    }

    private static (SamplingCommand Command, Mock<IExecutionSettingsProvider> Provider, ChatCommandContext Context) Arrange(
        string arguments = "",
        bool withProvider = true,
        float? temperature = null,
        int? maxTokens = null) {
        var provider = CreateProvider(temperature, maxTokens);
        var services = CommandTestHarness.CreateServices(
            executionSettingsProvider: withProvider ? provider.Object : null);
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new SamplingCommand(), provider, context);
    }

    [Fact]
    public async Task Execute_WithoutProvider_ShowsEngineNotReadyError() {
        var (cmd, provider, context) = Arrange(arguments: "0.5", withProvider: false);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("采样参数失败");
        output.Should().Contain("引擎未就绪");
        provider.VerifySet(p => p.Temperature = It.IsAny<float?>(), Times.Never,
            "提供者缺失时不应写任何设置");
    }

    [Fact]
    public async Task Execute_EmptyArgs_ShowsCurrentValues() {
        var (cmd, _, context) = Arrange(arguments: "", temperature: 0.7f, maxTokens: 2048);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("采样参数: 温度 0.70, 最大 2048 tokens");
    }

    [Fact]
    public async Task Execute_EmptyArgs_Defaults_ShowsDefaultLabels() {
        var (cmd, _, context) = Arrange(arguments: "");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("采样参数: 温度 默认, 最大 默认 tokens");
    }

    [Fact]
    public async Task Execute_Unset_ClearsBothOverrides() {
        var (cmd, provider, context) = Arrange(arguments: "unset", temperature: 0.9f, maxTokens: 512);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        provider.Object.Temperature.Should().BeNull("unset 应清除温度覆盖");
        provider.Object.MaxTokens.Should().BeNull("unset 应清除最大 Token 覆盖");
        output.Should().Contain("采样参数已重置为引擎默认值");
    }

    [Fact]
    public async Task Execute_UnsetArg_IsCaseInsensitive() {
        var (cmd, provider, context) = Arrange(arguments: "UNSET", temperature: 0.9f);

        await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        provider.Object.Temperature.Should().BeNull("unset 匹配应 OrdinalIgnoreCase");
    }

    [Fact]
    public async Task Execute_TemperatureAndMaxTokens_UpdatesBoth() {
        var (cmd, provider, context) = Arrange(arguments: "1.5 4096");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        provider.Object.Temperature.Should().Be(1.5f);
        provider.Object.MaxTokens.Should().Be(4096);
        output.Should().Contain("采样参数已更新: 温度 1.50, 最大 4096 tokens");
    }

    [Fact]
    public async Task Execute_TemperatureOnly_KeepsExistingMaxTokens() {
        var (cmd, provider, context) = Arrange(arguments: "0.8", maxTokens: 1024);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        provider.Object.Temperature.Should().Be(0.8f);
        provider.Object.MaxTokens.Should().Be(1024, "只给温度时最大 Token 应保持原值(对齐 GUI 单滑块语义)");
        output.Should().Contain("采样参数已更新: 温度 0.80, 最大 1024 tokens");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("3")]
    [InlineData("-0.5")]
    public async Task Execute_InvalidTemperature_ShowsRangeErrorWithoutUpdate(string badTemp) {
        var (cmd, provider, context) = Arrange(arguments: badTemp, temperature: 0.4f);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain($"无效温度: {badTemp}");
        output.Should().Contain("有效范围 0-2");
        provider.Object.Temperature.Should().Be(0.4f, "无效温度不应覆盖原值");
    }

    [Theory]
    [InlineData("0.7 0")]
    [InlineData("0.7 -5")]
    [InlineData("0.7 abc")]
    public async Task Execute_InvalidMaxTokens_ShowsErrorWithoutMaxUpdate(string args) {
        var (cmd, provider, context) = Arrange(arguments: args, maxTokens: 256);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("无效最大 Token 数");
        provider.Object.MaxTokens.Should().Be(256, "无效最大 Token 不应覆盖原值");
    }

    [Fact]
    public async Task Execute_BoundaryTemperatures_AreValid() {
        var (cmd, provider, context) = Arrange(arguments: "0 100");
        var (cmd2, provider2, context2) = Arrange(arguments: "2 100");

        var (_, output1) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));
        var (_, output2) = await CommandTestHarness.CaptureOutputAsync(() => cmd2.ExecuteAsync(context2));

        provider.Object.Temperature.Should().Be(0f, "下界 0 应合法");
        provider2.Object.Temperature.Should().Be(2f, "上界 2 应合法");
        output1.Should().Contain("采样参数已更新: 温度 0.00, 最大 100 tokens");
        output2.Should().Contain("采样参数已更新: 温度 2.00, 最大 100 tokens");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new SamplingCommand();
        cmd.Name.Should().Be("sampling");
        cmd.Usage.Should().Be("/sampling [温度] [最大Token|unset]");
    }
}
