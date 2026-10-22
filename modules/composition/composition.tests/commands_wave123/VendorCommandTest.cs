namespace Composition.Tests.Commands;

/// <summary>
/// /vendor 命令单元测试 — 覆盖无参/list/ls 别名列供应商并标记当前/未知供应商错误引导/
/// 相同供应商免切换/跨供应商切换(默认模型同步+profile 持久化)/无默认模型保持原模型/
/// WorkflowConfig 缺失引擎未就绪降级/catalog 缺失抛 [APP003](bug 登记快照)
/// </summary>
[Collection("command-output")]
public sealed class VendorCommandTest {
    private static Mock<IModelCatalog> CreateCatalog(string defaultModel = "deepseek-chat") {
        var catalog = new Mock<IModelCatalog>();
        catalog.Setup(c => c.GetDefaultModelForProvider(It.IsAny<string>())).Returns(defaultModel);
        return catalog;
    }

    private static Mock<IFastModeService> CreateFastMode() {
        var fastMode = new Mock<IFastModeService>();
        fastMode.Setup(f => f.PrimaryModelId).Returns("gpt-4o");
        fastMode.Setup(f => f.IsFastModeActive).Returns(false);
        return fastMode;
    }

    private static WorkflowConfig CreateConfig(string vendor = "openai", string modelId = "gpt-4o") => new() {
        Provider = new ProviderConfig { Vendor = vendor, ModelId = modelId },
    };

    private static (VendorCommand Command, ChatCommandContext Context, Mock<IConfigurationService> Config) Arrange(
        string arguments,
        Mock<IModelCatalog>? catalog = null,
        Mock<IFastModeService>? fastMode = null,
        WorkflowConfig? workflowConfig = null) {
        var config = new Mock<IConfigurationService>();
        var services = CommandTestHarness.CreateServices(
            workflowConfig: workflowConfig,
            configureDi: di => {
                di.AddSingleton(config.Object);
                if (fastMode is not null) di.AddSingleton(fastMode.Object);
                if (catalog is not null) di.AddSingleton(catalog.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new VendorCommand(), context, config);
    }

    [Fact]
    public async Task Execute_NoArgs_ListsAllVendorsWithCurrentMarker() {
        var (cmd, context, _) = Arrange("", workflowConfig: CreateConfig());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 可用供应商 ===");
        output.Should().Contain("openai ← 当前", "当前供应商应带标记");
        output.Should().Contain("anthropic");
        output.Should().Contain("deepseek");
        output.Should().Contain("zhipu");
    }

    [Theory]
    [InlineData("list")]
    [InlineData("ls")]
    public async Task Execute_ListAliases_ListVendors(string arguments) {
        var (cmd, context, _) = Arrange(arguments, workflowConfig: CreateConfig());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 可用供应商 ===", $"{arguments} 应作为列表别名");
        output.Should().Contain("openai ← 当前");
    }

    [Fact]
    public async Task Execute_UnknownVendor_ShowsErrorWithGuidance() {
        var (cmd, context, _) = Arrange("bogus", workflowConfig: CreateConfig());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("未知供应商: bogus。输入 /vendor 查看可用列表",
            "错误提示应携带正确写法引导(规则9)");
    }

    [Fact]
    public async Task Execute_SameVendor_ShowsNoSwitchNeeded() {
        var fastMode = CreateFastMode();
        var (cmd, context, config) = Arrange("openai", fastMode: fastMode, workflowConfig: CreateConfig());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("当前已是 openai，无需切换");
        config.Verify(c => c.SetAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never,
            "相同供应商不应触发持久化");
    }

    [Fact]
    public async Task Execute_DifferentVendor_SwitchesAndSyncsDefaultModel() {
        var fastMode = CreateFastMode();
        var (cmd, context, config) = Arrange("deepseek", catalog: CreateCatalog(), fastMode: fastMode, workflowConfig: CreateConfig());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已切换供应商: openai → deepseek（默认模型: deepseek-chat）");
        var effectiveConfig = context.GetCommandServices().WorkflowConfig;
        effectiveConfig.Should().NotBeNull();
        Assert.NotNull(effectiveConfig);
        effectiveConfig.Provider.Vendor.Should().Be("deepseek", "切换应更新 WorkflowConfig 供应商");
        effectiveConfig.Provider.ModelId.Should().Be("deepseek-chat", "默认模型应跟随供应商切换(对齐 GUI SetVendorAsync 语义)");
        fastMode.Verify(f => f.SetPrimaryModel("deepseek-chat"), Times.Once);
        config.Verify(c => c.SetAsync(ConfigKeyEnumConstants.Profile, "deepseek", It.IsAny<CancellationToken>()), Times.Once,
            "切换应持久化 profile");
    }

    [Fact]
    public async Task Execute_SwitchWithoutDefaultModel_KeepsCurrentModelId() {
        var fastMode = CreateFastMode();
        var (cmd, context, config) = Arrange("deepseek", catalog: CreateCatalog(defaultModel: ""), fastMode: fastMode, workflowConfig: CreateConfig());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已切换供应商: openai → deepseek");
        output.Should().NotContain("默认模型:", "无默认模型时不应显示默认模型括号");
        var effectiveConfig = context.GetCommandServices().WorkflowConfig;
        Assert.NotNull(effectiveConfig);
        effectiveConfig.Provider.ModelId.Should().Be("gpt-4o", "供应商无默认模型时应保持原模型 ID");
        fastMode.Verify(f => f.SetPrimaryModel(It.IsAny<string>()), Times.Never,
            "无默认模型时不应同步 FastModeService 主模型");
        config.Verify(c => c.SetAsync(ConfigKeyEnumConstants.Profile, "deepseek", It.IsAny<CancellationToken>()), Times.Once,
            "profile 持久化不受默认模型影响,始终执行");
    }

    [Fact]
    public async Task Execute_WithoutWorkflowConfig_ShowsEngineNotReady() {
        var (cmd, context, _) = Arrange("deepseek", workflowConfig: null);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("WorkflowConfig 缺失应降级提示而非崩溃");
        output.Should().Contain("供应商切换失败: 引擎未就绪：缺少 WorkflowConfig");
    }

    /// <summary>生产 bug 已修复(主代理): ModelCatalog 缺失(精简启动形态)时 /vendor &lt;name&gt;
    /// 降级切换成功（默认模型保持原值不跟随 + profile 照常持久化 — 不再抛 [APP003] 传播 REPL，
    /// 对齐 ModelCommand/EffortCommand 降级先例）。</summary>
    [Fact]
    public async Task Execute_SwitchWithoutCatalog_SwitchesKeepingCurrentModel() {
        var (cmd, context, _) = Arrange("deepseek", catalog: null, workflowConfig: CreateConfig());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("目录缺失属降级场景非命令失败");
        output.Should().Contain("已切换供应商", "目录缺失降级仍可切换 vendor");
        output.Should().NotContain("默认模型", "默认模型保持原值不跟随（目录缺失无法解析）");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new VendorCommand();
        cmd.Name.Should().Be("vendor");
        cmd.Usage.Should().Be("/vendor [名称|list]");
        cmd.ArgumentHint.Should().StartWith("[openai|anthropic|deepseek");
        cmd.Description.Should().Contain("供应商");
    }
}
