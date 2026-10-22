namespace Composition.Tests.Commands;

/// <summary>
/// /advisor 命令单元测试 — 覆盖空参状态查询(启用/禁用)/off 与 unset 关闭/
/// 有效模型设置/未知模型警告仍设置/服务缺失静默/元数据一致性
/// <para>AdvisorService 用 Moq 桩经 DI 注入;ModelConfigLoader 用 Moq 桩构造注入</para>
/// </summary>
[Collection("command-output")]
public sealed class AdvisorCommandTest {
    private static Mock<IModelConfigLoader> LoaderWithModels(params string[] modelIds) {
        var loader = new Mock<IModelConfigLoader>();
        loader.Setup(l => l.GetAllModelIds()).Returns(modelIds);
        return loader;
    }

    private static (AdvisorCommand Command, ChatCommandContext Context, Mock<IAdvisorService> Service) Arrange(
        string arguments = "",
        Mock<IAdvisorService>? service = null,
        Mock<IModelConfigLoader>? loader = null) {
        var advisorService = service ?? new Mock<IAdvisorService>();
        var services = CommandTestHarness.CreateServices(
            configureDi: di => di.AddSingleton(advisorService.Object));
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new AdvisorCommand(loader?.Object), context, advisorService);
    }

    [Fact]
    public async Task Execute_EmptyArgs_AdvisorEnabled_ShowsModelAndReviewHint() {
        var service = new Mock<IAdvisorService>();
        service.Setup(s => s.GetIsAdvisorEnabledAsync()).ReturnsAsync(true);
        service.Setup(s => s.GetAdvisorModelAsync()).ReturnsAsync("gpt-4o");
        var (cmd, context, _) = Arrange(service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("顾问模型: gpt-4o");
        output.Should().Contain("顾问将审查主模型的工作");
    }

    [Fact]
    public async Task Execute_EmptyArgs_AdvisorDisabled_ShowsEnableHint() {
        var service = new Mock<IAdvisorService>();
        service.Setup(s => s.GetIsAdvisorEnabledAsync()).ReturnsAsync(false);
        var (cmd, context, _) = Arrange(service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("顾问模式：未启用");
        output.Should().Contain("使用 /advisor <model> 启用顾问", "禁用状态应引导用户启用方式");
    }

    [Theory]
    [InlineData("off")]
    [InlineData("unset")]
    [InlineData("OFF")]
    [InlineData("Unset")]
    public async Task Execute_OffOrUnset_ClearsAdvisorModel(string arguments) {
        var service = new Mock<IAdvisorService>();
        var (cmd, context, _) = Arrange(arguments: arguments, service: service);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        service.Verify(s => s.ClearAdvisorModel(), Times.Once, $"{arguments} 应关闭顾问模式(大小写不敏感)");
        output.Should().Contain("顾问模式：已禁用");
    }

    [Fact]
    public async Task Execute_KnownModel_SetsAdvisorModelDirectly() {
        var service = new Mock<IAdvisorService>();
        var loader = LoaderWithModels("gpt-4o", "claude-3");
        var (cmd, context, _) = Arrange(arguments: "claude-3", service: service, loader: loader);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        service.Verify(s => s.SetAdvisorModel("claude-3"), Times.Once, "已知模型应直接设置");
        output.Should().Contain("顾问模型: claude-3");
        output.Should().Contain("顾问将审查主模型的工作");
        output.Should().NotContain("已知支持模型：", "已知模型不应触发支持列表警告");
    }

    [Fact]
    public async Task Execute_UnknownModel_WarnsButStillSets() {
        var service = new Mock<IAdvisorService>();
        var loader = LoaderWithModels("gpt-4o", "claude-3");
        var (cmd, context, _) = Arrange(arguments: "llama-x", service: service, loader: loader);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("模型 'llama-x' 可能不支持顾问功能", "未知模型应给出警告");
        output.Should().Contain("已知支持模型：");
        output.Should().Contain("  gpt-4o");
        output.Should().Contain("  claude-3", "警告应列出全部已知模型辅助选择");
        output.Should().Contain("确定设置？输入 /advisor <model> 确认");
        service.Verify(s => s.SetAdvisorModel("llama-x"), Times.Once,
            "未知模型仍执行设置(警告不拦截,交由用户确认)");
    }

    [Fact]
    public async Task Execute_UnknownModel_WithoutLoader_StillSetsModel() {
        var service = new Mock<IAdvisorService>();
        var (cmd, context, _) = Arrange(arguments: "any-model", service: service, loader: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        service.Verify(s => s.SetAdvisorModel("any-model"), Times.Once,
            "模型列表加载器缺失(空列表)时仍应设置模型");
        output.Should().Contain("顾问模型: any-model");
    }

    [Fact]
    public async Task Execute_WithoutService_ReturnsSilently() {
        var services = CommandTestHarness.CreateServices();
        var context = CommandTestHarness.CreateContext(services, arguments: "gpt-4o");

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(
            () => new AdvisorCommand().ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("服务缺失应静默返回而非崩溃");
        output.Should().NotContain("顾问模型:", "无服务时不应渲染顾问状态");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new AdvisorCommand();
        cmd.Name.Should().Be("advisor");
        cmd.Usage.Should().Be("/advisor [model|off]");
        cmd.Description.Should().Contain("顾问模型");
        cmd.ArgumentHint.Should().Be("[model|off]");
    }
}
