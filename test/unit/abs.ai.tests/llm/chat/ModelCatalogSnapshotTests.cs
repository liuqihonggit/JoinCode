namespace Abs.Tests.LLM;

/// <summary>
/// ModelCatalog 供应商目录/快照单元测试 — 验证 UI 读取模型清单的唯一形状：
/// 供应商集合来自注册表（顺序保持）、显示名与默认模型透传、availableModels 白名单过滤、
/// 环境变量自定义模型追加，以及快照对当前供应商/当前模型的补全语义。
/// </summary>
public class ModelCatalogSnapshotTests {
    private sealed record ProviderSpec(string Id, string DisplayName, string DefaultModelId, string DefaultFastModelId, ModelEntry[] Models);

    private static ProviderSpec OpenAiSpec => new(
        "openai", "OpenAI", "gpt-4o", "gpt-4o-mini",
        [
            new ModelEntry("gpt-4o", "GPT-4o", 128_000),
            new ModelEntry("gpt-4o-mini", "GPT-4o Mini", 128_000),
        ]);

    private static ProviderSpec AnthropicSpec => new(
        "anthropic", "Anthropic", "claude-sonnet-4-6", "claude-haiku-4-5",
        [new ModelEntry("claude-sonnet-4-6", "Claude Sonnet 4.6", 200_000)]);

    private static IProviderDefinitionRegistry CreateRegistry(params ProviderSpec[] specs) {
        var definitions = new Dictionary<string, IProviderDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs) {
            var definition = new Mock<IProviderDefinition>();
            definition.SetupGet(d => d.DisplayName).Returns(spec.DisplayName);
            definition.SetupGet(d => d.DefaultModelId).Returns(spec.DefaultModelId);
            definition.SetupGet(d => d.DefaultFastModelId).Returns(spec.DefaultFastModelId);
            definition.SetupGet(d => d.AvailableModels).Returns(spec.Models);
            definitions[spec.Id] = definition.Object;
        }

        var registry = new Mock<IProviderDefinitionRegistry>();
        registry.Setup(r => r.GetRegisteredProviders()).Returns(specs.Select(s => s.Id).ToArray());
        registry.Setup(r => r.TryGet(It.IsAny<string>()))
            .Returns((string id) => definitions.TryGetValue(id, out var definition) ? definition : null);
        registry.Setup(r => r.Contains(It.IsAny<string>()))
            .Returns((string id) => definitions.ContainsKey(id));
        return registry.Object;
    }

    private static ModelCatalog CreateCatalog(IProviderDefinitionRegistry registry, WorkflowConfig? workflowConfig = null)
        => new(registry, workflowConfig: workflowConfig);

    private static EnvVarScope ClearCustomModelEnv()
        => EnvVarScope.Set(JccEnvVar.CustomModelOption.ToValue(), null)
            .Add(JccEnvVar.CustomModelOptionName.ToValue(), null)
            .Add(JccEnvVar.CustomModelOptionDescription.ToValue(), null);

    #region GetProviders

    [Fact]
    public async Task GetProviders_ReturnsRegisteredProvidersInRegistryOrder() {
        using var env = ClearCustomModelEnv();
        await using var catalog = CreateCatalog(CreateRegistry(OpenAiSpec, AnthropicSpec));

        var providers = catalog.GetProviders();

        providers.Select(p => p.Id).Should().Equal("openai", "anthropic");
        providers[0].DisplayName.Should().Be("OpenAI");
        providers[0].DefaultModelId.Should().Be("gpt-4o");
        providers[0].DefaultFastModelId.Should().Be("gpt-4o-mini");
        providers[0].Models.Should().HaveCount(2);
        providers[1].DisplayName.Should().Be("Anthropic");
        providers[1].DefaultModelId.Should().Be("claude-sonnet-4-6");
        providers[1].DefaultFastModelId.Should().Be("claude-haiku-4-5");
    }

    [Fact]
    public async Task GetProviders_AppliesAvailableModelsWhitelist() {
        using var env = ClearCustomModelEnv();
        var workflow = new WorkflowConfig { AvailableModels = ["gpt-4o"] };
        await using var catalog = CreateCatalog(CreateRegistry(OpenAiSpec, AnthropicSpec), workflow);

        var providers = catalog.GetProviders();

        providers[0].Models.Select(m => m.Id).Should().Equal("gpt-4o");
        providers[1].Models.Should().BeEmpty();
    }

    [Fact]
    public async Task GetProviders_AppendsCustomModelFromEnvironment() {
        using var env = EnvVarScope.Set(JccEnvVar.CustomModelOption.ToValue(), "my-custom-model")
            .Add(JccEnvVar.CustomModelOptionName.ToValue(), "My Custom Model")
            .Add(JccEnvVar.CustomModelOptionDescription.ToValue(), "团队私有部署");
        await using var catalog = CreateCatalog(CreateRegistry(OpenAiSpec));

        var providers = catalog.GetProviders();

        var custom = providers[0].Models.Single(m => m.Id == "my-custom-model");
        custom.DisplayName.Should().Be("My Custom Model");
        custom.Description.Should().Be("团队私有部署");
    }

    #endregion

    #region GetSnapshot

    [Fact]
    public async Task GetSnapshot_EnsuresCurrentModelInCurrentProvider() {
        using var env = ClearCustomModelEnv();
        await using var catalog = CreateCatalog(CreateRegistry(OpenAiSpec, AnthropicSpec));

        var snapshot = catalog.GetSnapshot("openai", "gpt-5-preview");

        snapshot.CurrentProvider.Should().Be("openai");
        snapshot.CurrentModelId.Should().Be("gpt-5-preview");
        snapshot.Providers.Should().HaveCount(2);
        snapshot.Providers[0].Models.Select(m => m.Id).Should().Contain("gpt-5-preview");
        snapshot.Providers[0].Models.Select(m => m.Id).Should().Contain(["gpt-4o", "gpt-4o-mini", "gpt-5-preview"]);
        snapshot.Providers[1].Models.Select(m => m.Id).Should().NotContain("gpt-5-preview");
    }

    [Fact]
    public async Task GetSnapshot_WhenCurrentModelAlreadyListed_DoesNotDuplicate() {
        using var env = ClearCustomModelEnv();
        await using var catalog = CreateCatalog(CreateRegistry(OpenAiSpec));

        var snapshot = catalog.GetSnapshot("openai", "gpt-4o");

        snapshot.Providers[0].Models.Select(m => m.Id).Should().Equal("gpt-4o", "gpt-4o-mini");
    }

    [Fact]
    public async Task GetSnapshot_UnknownCurrentProvider_AddsPlaceholderGroup() {
        using var env = ClearCustomModelEnv();
        await using var catalog = CreateCatalog(CreateRegistry(OpenAiSpec));

        var snapshot = catalog.GetSnapshot("jev", "jev-1");

        snapshot.Providers.Should().HaveCount(2);
        var placeholder = snapshot.Providers[^1];
        placeholder.Id.Should().Be("jev");
        placeholder.DisplayName.Should().Be("jev");
        placeholder.Models.Select(m => m.Id).Should().Equal("jev-1");
    }

    [Fact]
    public async Task GetSnapshot_EmptyCurrentProvider_DoesNotAddPlaceholder() {
        using var env = ClearCustomModelEnv();
        await using var catalog = CreateCatalog(CreateRegistry(OpenAiSpec));

        var snapshot = catalog.GetSnapshot(string.Empty, "gpt-4o");

        snapshot.CurrentProvider.Should().BeEmpty();
        snapshot.Providers.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSnapshot_MatchesCurrentProviderCaseInsensitively() {
        using var env = ClearCustomModelEnv();
        await using var catalog = CreateCatalog(CreateRegistry(OpenAiSpec));

        var snapshot = catalog.GetSnapshot("OpenAI", "gpt-5-preview");

        snapshot.Providers.Should().HaveCount(1);
        snapshot.Providers[0].Id.Should().Be("openai");
        snapshot.Providers[0].Models.Select(m => m.Id).Should().Contain("gpt-5-preview");
    }

    #endregion
}
