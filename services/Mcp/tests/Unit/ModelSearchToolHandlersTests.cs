namespace Mcp.Tests;

public sealed class ModelSearchToolHandlersTests
{
    [Fact]
    public async Task SearchModelsAsync_EmptyQuery_ReturnsError()
    {
        var handler = new ModelSearchToolHandlers(new FakeModelConfigLoader());
        var result = await handler.SearchModelsAsync("");
        result.IsError.Should().BeTrue();
        result.GetFirstText().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SearchModelsAsync_WhitespaceQuery_ReturnsError()
    {
        var handler = new ModelSearchToolHandlers(new FakeModelConfigLoader());
        var result = await handler.SearchModelsAsync("   ");
        result.IsError.Should().BeTrue();
    }

    [Fact]
    public async Task SearchModelsAsync_ListGroups_ReturnsFunctionalities()
    {
        var loader = new FakeModelConfigLoader();
        loader.AddModel("openai", "gpt-4o", "GPT-4o", ModelModalityKind.Text | ModelModalityKind.ReadImage);
        loader.AddModel("openai", "dall-e-3", "DALL-E 3", ModelModalityKind.GenerateImage);
        var handler = new ModelSearchToolHandlers(loader);

        var result = await handler.SearchModelsAsync("list_groups");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("readImage:图片识别");
        text.Should().Contain("generateImage:图片生成");
    }

    [Fact]
    public async Task SearchModelsAsync_MapByModality_ReturnsModels()
    {
        var loader = new FakeModelConfigLoader();
        loader.AddModel("openai", "dall-e-3", "DALL-E 3", ModelModalityKind.GenerateImage);
        var handler = new ModelSearchToolHandlers(loader);

        var result = await handler.SearchModelsAsync("map[generateImage]");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("dall-e-3");
        result.GetFirstText().Should().Contain("DALL-E 3");
    }

    [Fact]
    public async Task SearchModelsAsync_MapByModalityAndVendor_FiltersVendor()
    {
        var loader = new FakeModelConfigLoader();
        loader.AddModel("openai", "gpt-4o", "GPT-4o", ModelModalityKind.ReadImage);
        loader.AddModel("anthropic", "claude-3", "Claude 3", ModelModalityKind.ReadImage);
        var handler = new ModelSearchToolHandlers(loader);

        var result = await handler.SearchModelsAsync("map[readImage][openai]");

        result.IsError.Should().BeFalse();
        var text = result.GetFirstText();
        text.Should().Contain("gpt-4o");
        text.Should().NotContain("claude-3");
    }

    [Fact]
    public async Task SearchModelsAsync_NoModels_ListGroups_ReturnsEmptyMessage()
    {
        var handler = new ModelSearchToolHandlers(new FakeModelConfigLoader());
        var result = await handler.SearchModelsAsync("list_groups");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task SearchModelsAsync_KeywordSearch_MatchesModelId()
    {
        var loader = new FakeModelConfigLoader();
        loader.AddModel("openai", "gpt-4o", "GPT-4o", ModelModalityKind.Text);
        loader.AddModel("openai", "dall-e-3", "DALL-E 3", ModelModalityKind.GenerateImage);
        var handler = new ModelSearchToolHandlers(loader);

        var result = await handler.SearchModelsAsync("dall");

        result.IsError.Should().BeFalse();
        result.GetFirstText().Should().Contain("dall-e-3");
    }

    private sealed class FakeModelConfigLoader : IModelConfigLoader
    {
        public ModelConfigRoot Config { get; } = new();

        public void AddModel(string vendor, string modelId, string displayName, ModelModalityKind modalities)
        {
            if (!Config.Providers.TryGetValue(vendor, out var provider))
            {
                provider = new ModelProviderConfig();
                Config.Providers[vendor] = provider;
            }
            provider.Models.Add(new ModelItemConfig
            {
                Id = modelId,
                DisplayName = displayName,
                Capabilities = new ModelCapabilitiesConfig { Modalities = modalities }
            });
        }

        public void ApplyProviders(Dictionary<string, ModelProviderConfig> providers) => throw new NotImplementedException();
        public ModelProviderConfig? GetProviderConfig(string providerName) => null;
        public string GetDefaultModelId(string providerName) => string.Empty;
        public string GetDefaultFastModelId(string providerName) => string.Empty;
        public ModelEntry[] GetModels(string providerName) => [];
        public string? ResolveAlias(string providerName, string input) => null;
        public bool SupportsFastMode(string providerName, string modelId) => false;
        public bool SupportsEffort(string providerName, string modelId) => false;
        public bool SupportsMaxEffort(string providerName, string modelId) => false;
        public bool SupportsThinkingMode(string providerName, string modelId) => false;
        public bool SupportsModality(string providerName, string modelId, ModelModalityKind modality) => false;
        public ModelModalityKind GetModalities(string providerName, string modelId) => ModelModalityKind.None;
        public string GetCanonicalName(string fullModelName) => fullModelName;
        public ModelItemConfig? FindModel(string providerName, string modelId) => null;
        public IReadOnlyCollection<string> GetAllModelIds() => [];
        public string? FindProviderByModelId(string modelId) => null;
        public ModelItemConfig? FindModelByModelId(string modelId) => null;
    }
}
