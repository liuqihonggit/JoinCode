namespace Composition.Tests.Commands;

/// <summary>
/// /model 命令单元测试 — 覆盖无参非交互文本列表/info 与 ? 别名模型信息/default 恢复默认+持久化/
/// 直接 ID 切换/别名解析/FastModeService 缺失环境变量回退/FastMode 不支持自动关闭/
/// max effort 不支持降级 high/modelHistory 去重排序/catalog 缺失抛 [APP003](bug 登记快照)
/// </summary>
[Collection("command-output")]
public sealed class ModelCommandTest {
    private static Mock<IModelCatalog> CreateCatalog(
        string providerDisplayName = "OpenAI",
        string defaultModel = "default-model",
        string? aliasResolution = null,
        bool supportsFastMode = true,
        bool supportsMaxEffort = true) {
        var catalog = new Mock<IModelCatalog>();
        catalog.Setup(c => c.GetProviderDisplayName(It.IsAny<string>())).Returns(providerDisplayName);
        catalog.Setup(c => c.GetModelsForProvider(It.IsAny<string>()))
            .Returns([new ModelEntry("model-a", "Model A", 200_000), new ModelEntry("model-b", "Model B", 128_000)]);
        catalog.Setup(c => c.EnsureCurrentModelInList(It.IsAny<ModelEntry[]>(), It.IsAny<string>()))
            .Returns((ModelEntry[] models, string _) => models);
        catalog.Setup(c => c.GetDefaultModelForProvider(It.IsAny<string>())).Returns(defaultModel);
        catalog.Setup(c => c.ResolveAlias(It.IsAny<string>(), It.IsAny<string>())).Returns(aliasResolution);
        catalog.Setup(c => c.SupportsFastMode(It.IsAny<string>(), It.IsAny<string>())).Returns(supportsFastMode);
        catalog.Setup(c => c.SupportsMaxEffort(It.IsAny<string>(), It.IsAny<string>())).Returns(supportsMaxEffort);
        return catalog;
    }

    private static Mock<IFastModeService> CreateFastMode(string primaryModel = "model-a", bool fastActive = false) {
        var fastMode = new Mock<IFastModeService>();
        fastMode.Setup(f => f.PrimaryModelId).Returns(primaryModel);
        fastMode.Setup(f => f.FastModelId).Returns("fast-model");
        fastMode.Setup(f => f.IsFastModeActive).Returns(fastActive);
        return fastMode;
    }

    private static (ModelCommand Command, ChatCommandContext Context, Mock<IExecutionSettingsProvider> Settings, Mock<IConfigurationService> Config) Arrange(
        string arguments,
        Mock<IModelCatalog>? catalog = null,
        Mock<IFastModeService>? fastMode = null,
        bool withConfigService = true,
        WorkflowConfig? workflowConfig = null) {
        var settings = new Mock<IExecutionSettingsProvider>();
        settings.SetupProperty(p => p.EffortLevel, EffortLevel.Auto);
        var config = new Mock<IConfigurationService>();
        var services = CommandTestHarness.CreateServices(
            executionSettingsProvider: settings.Object,
            workflowConfig: workflowConfig,
            configureDi: di => {
                if (withConfigService) di.AddSingleton(config.Object);
                if (fastMode is not null) di.AddSingleton(fastMode.Object);
                if (catalog is not null) di.AddSingleton(catalog.Object);
            });
        var context = CommandTestHarness.CreateContext(services, arguments: arguments);
        return (new ModelCommand(), context, settings, config);
    }

    [Fact]
    public async Task Execute_NoArgs_NonInteractive_ShowsTextModelList() {
        var (cmd, context, _, _) = Arrange("", catalog: CreateCatalog(), fastMode: CreateFastMode());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 模型列表 (OpenAI) ===");
        output.Should().Contain("1. Model A");
        output.Should().Contain("2. Model B");
        output.Should().Contain("当前模型: model-a");
        output.Should().Contain("Effort: auto | Fast mode: OFF",
            "非交互(测试环境)回退文本列表模式,显示 effort 与 fast mode 状态");
    }

    [Fact]
    public async Task Execute_Info_ShowsCurrentModelInfo() {
        var (cmd, context, _, _) = Arrange("info", catalog: CreateCatalog(), fastMode: CreateFastMode());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 当前模型信息 ===");
        output.Should().Contain("Provider: OpenAI");
        output.Should().Contain("主模型: model-a");
        output.Should().Contain("快速模型: fast-model");
        output.Should().Contain("快速模式: 关闭");
        output.Should().Contain("Effort 等级: auto");
        output.Should().Contain("可用模型数: 2");
    }

    [Fact]
    public async Task Execute_QuestionMarkAlias_ShowsModelInfo() {
        var (cmd, context, _, _) = Arrange("?", catalog: CreateCatalog(), fastMode: CreateFastMode());

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("=== 当前模型信息 ===", "? 应作为 info 的别名走模型信息分支");
    }

    [Fact]
    public async Task Execute_Default_SwitchesToDefaultModelAndPersists() {
        var catalog = CreateCatalog(defaultModel: "default-model");
        var fastMode = CreateFastMode();
        var (cmd, context, _, config) = Arrange("default", catalog: catalog, fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已恢复默认模型: default-model");
        fastMode.Verify(f => f.SetPrimaryModel("default-model"), Times.Once,
            "default 应把主模型切换为供应商默认模型");
        config.Verify(c => c.SetAsync("model", "default-model", It.IsAny<CancellationToken>()), Times.Once,
            "切换应持久化到 settings.json(键 model 对齐 TS userSettings)");
        config.Verify(c => c.SetAsync("modelHistory", "default-model", It.IsAny<CancellationToken>()), Times.Once,
            "首次切换应记录模型选择历史");
    }

    [Fact]
    public async Task Execute_ModelId_SwitchesAndPersists() {
        var fastMode = CreateFastMode();
        var (cmd, context, _, config) = Arrange("my-model", catalog: CreateCatalog(aliasResolution: null), fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已切换模型: my-model");
        fastMode.Verify(f => f.SetPrimaryModel("my-model"), Times.Once);
        config.Verify(c => c.SetAsync("model", "my-model", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_Alias_ResolvesToCanonicalModelId() {
        var catalog = CreateCatalog(aliasResolution: "claude-opus-4");
        var fastMode = CreateFastMode();
        var (cmd, context, _, _) = Arrange("opus", catalog: catalog, fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        output.Should().Contain("已切换模型: claude-opus-4", "别名应经 catalog 解析为规范模型 ID");
        fastMode.Verify(f => f.SetPrimaryModel("claude-opus-4"), Times.Once);
    }

    [Fact]
    public async Task Execute_WithoutFastModeService_SetsModelIdEnvVar() {
        var prevEnv = Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue());
        try {
            var (cmd, context, _, _) = Arrange("env-model", catalog: CreateCatalog(), fastMode: null, withConfigService: false);

            var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

            result.ShouldContinue.Should().BeTrue();
            output.Should().Contain("已切换模型: env-model");
            Environment.GetEnvironmentVariable(JccEnvVar.ModelId.ToValue()).Should().Be("env-model",
                "FastModeService 缺失时应回退写 JCC_MODEL_ID 环境变量");
        } finally {
            Environment.SetEnvironmentVariable(JccEnvVar.ModelId.ToValue(), prevEnv);
        }
    }

    [Fact]
    public async Task Execute_FastModeUnsupportedModel_DeactivatesFastMode() {
        var catalog = CreateCatalog(supportsFastMode: false);
        var fastMode = CreateFastMode(primaryModel: "model-a", fastActive: true);
        var (cmd, context, _, _) = Arrange("model-x", catalog: catalog, fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        fastMode.Verify(f => f.Deactivate(), Times.Once,
            "切换到不支持快速模式的模型应自动关闭 FastMode(对齐 TS handleFastModeAutoOff)");
        output.Should().Contain("模型 model-x 不支持快速模式，已自动关闭");
    }

    [Fact]
    public async Task Execute_MaxEffortUnsupportedModel_DowngradesToHigh() {
        var catalog = CreateCatalog(supportsMaxEffort: false);
        var fastMode = CreateFastMode();
        var (cmd, context, settings, _) = Arrange("model-x", catalog: catalog, fastMode: fastMode);
        settings.Object.EffortLevel = EffortLevel.Max;

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        settings.Object.EffortLevel.Should().Be(EffortLevel.High,
            "切换到不支持 max effort 的模型应降级为 high(对齐 TS effortAutoDowngrade)");
        output.Should().Contain("模型 model-x 不支持 max effort，已降级为 high");
    }

    [Fact]
    public async Task Execute_ModelHistory_DeduplicatesAndMovesToFront() {
        var catalog = CreateCatalog();
        var fastMode = CreateFastMode();
        var (cmd, context, _, config) = Arrange("b", catalog: catalog, fastMode: fastMode);
        config.Setup(c => c.GetAsync("modelHistory", It.IsAny<CancellationToken>()))
            .ReturnsAsync("a,b,c");

        var (result, _) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue();
        config.Verify(c => c.SetAsync("modelHistory", "b,c,a", It.IsAny<CancellationToken>()), Times.Once,
            "历史去重后把最新选择移到头部: a,b,c 切 b → b,c,a");
    }

    /// <summary>生产 bug 已修复(主代理): ModelCatalog 缺失(精简启动形态)时 /model &lt;id&gt;
    /// 别名解析降级原样透传 + 直切成功（能力判定保守跳过 — 不再抛 [APP003] 传播 REPL，
    /// 对齐 EffortCommand 降级先例）。</summary>
    [Fact]
    public async Task Execute_SwitchWithoutCatalog_SwitchesWithRawId() {
        var fastMode = CreateFastMode();
        var (cmd, context, _, _) = Arrange("some-model", catalog: null, fastMode: fastMode);

        var (result, output) = await CommandTestHarness.CaptureOutputAsync(() => cmd.ExecuteAsync(context));

        result.ShouldContinue.Should().BeTrue("目录缺失属降级场景非命令失败");
        output.Should().Contain("已切换模型: some-model", "别名解析降级原样透传直切");
    }

    [Fact]
    public void NameAndUsage_MetadataShouldBeConsistent() {
        var cmd = new ModelCommand();
        cmd.Name.Should().Be("model");
        cmd.Usage.Should().Be("/model [model-id|default|info]");
        cmd.ArgumentHint.Should().Be("[model-id|default|info]");
        cmd.Description.Should().Contain("切换或查看模型");
    }
}
