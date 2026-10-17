namespace Hands.Api.Tests.WebCatalog;

public sealed class ProviderCatalogTests {
    private sealed class FakeLlmDefinition : IProviderDefinition {
        public string ProviderName { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public VendorKind Vendor { get; init; }
        public ProtocolKind Protocol { get; init; }
        public string DefaultModelId => "";
        public string DefaultFastModelId => "";
        public string? DefaultEndpoint => null;
        public IEnumerable<ModelEntry> AvailableModels => [];
        public string? ApiKeyEnvironmentVariable => null;
        public string? EndpointEnvironmentVariable => null;
        public string? ResolveApiKeyFromEnv() => _apiKey;
        public bool IsValid(ProviderConfig config) => true;
        public string GetBaseUrl(ProviderConfig config) => "";
        public string GetChatEndpoint(ProviderConfig config) => "";
        public void ConfigureHttpClient(HttpClient client, ProviderConfig config) { }
        private readonly string? _apiKey;
        public FakeLlmDefinition(string name, string displayName, ProtocolKind protocol, string? apiKey) {
            ProviderName = name;
            DisplayName = displayName;
            Protocol = protocol;
            _apiKey = apiKey;
        }
    }

    private sealed class FakeLlmRegistry : IProviderDefinitionRegistry {
        private readonly Dictionary<string, IProviderDefinition> _defs;
        public FakeLlmRegistry(params IProviderDefinition[] defs) {
            _defs = defs.ToDictionary(d => d.ProviderName, StringComparer.OrdinalIgnoreCase);
        }
        public IProviderDefinition? TryGet(string providerName)
            => _defs.TryGetValue(providerName, out var d) ? d : null;
        public bool Contains(string providerName) => _defs.ContainsKey(providerName);
        public string[] GetRegisteredProviders() => _defs.Keys.ToArray();
    }

    private sealed class FakeSearchProvider : ISearchProvider {
        public string Name { get; }
        public int Priority { get; }
        public bool IsAvailable { get; }
        public FakeSearchProvider(string name, int priority, bool available) {
            Name = name;
            Priority = priority;
            IsAvailable = available;
        }
        public Task<SearchProviderResult> SearchAsync(SearchRequest request, CancellationToken cancellationToken = default)
            => Task.FromResult(new SearchProviderResult(false, Name, [], 0, "fake"));
    }

    [Fact]
    public void GetAll_ReturnsLlmAndSearchProviders() {
        var llmRegistry = new FakeLlmRegistry(
            new FakeLlmDefinition("openai", "OpenAI", ProtocolKind.OpenAiCompatible, "sk-test"),
            new FakeLlmDefinition("anthropic", "Anthropic", ProtocolKind.Anthropic, "sk-test")
        );
        var searchProviders = new ISearchProvider[] {
            new FakeSearchProvider("Tavily", 100, true),
            new FakeSearchProvider("SubAgent", 999, true),
        };
        var catalog = new Services.Web.ProviderCatalog.ProviderCatalog(llmRegistry, searchProviders);

        var all = catalog.GetAll();

        Assert.Equal(4, all.Count);
        Assert.Contains(all, d => d.Name == "openai" && d.Category == ProviderCategory.Llm);
        Assert.Contains(all, d => d.Name == "anthropic" && d.Category == ProviderCategory.Llm);
        Assert.Contains(all, d => d.Name == "Tavily" && d.Category == ProviderCategory.Search);
        Assert.Contains(all, d => d.Name == "SubAgent" && d.Category == ProviderCategory.Search);
    }

    [Fact]
    public void GetByCategory_Llm_ReturnsOnlyLlm() {
        var llmRegistry = new FakeLlmRegistry(
            new FakeLlmDefinition("openai", "OpenAI", ProtocolKind.OpenAiCompatible, "sk-test")
        );
        var searchProviders = new ISearchProvider[] { new FakeSearchProvider("Tavily", 100, true) };
        var catalog = new Services.Web.ProviderCatalog.ProviderCatalog(llmRegistry, searchProviders);

        var llms = catalog.GetByCategory(ProviderCategory.Llm);

        Assert.Single(llms);
        Assert.Equal("openai", llms[0].Name);
    }

    [Fact]
    public void GetByCategory_Search_ReturnsOnlySearch() {
        var llmRegistry = new FakeLlmRegistry(
            new FakeLlmDefinition("openai", "OpenAI", ProtocolKind.OpenAiCompatible, "sk-test")
        );
        var searchProviders = new ISearchProvider[] {
            new FakeSearchProvider("Tavily", 100, true),
            new FakeSearchProvider("Serper", 200, true),
        };
        var catalog = new Services.Web.ProviderCatalog.ProviderCatalog(llmRegistry, searchProviders);

        var searches = catalog.GetByCategory(ProviderCategory.Search);

        Assert.Equal(2, searches.Count);
    }

    [Fact]
    public void TryGet_ExistingName_ReturnsTrueAndDescriptor() {
        var llmRegistry = new FakeLlmRegistry(
            new FakeLlmDefinition("openai", "OpenAI", ProtocolKind.OpenAiCompatible, "sk-test")
        );
        var catalog = new Services.Web.ProviderCatalog.ProviderCatalog(llmRegistry, Array.Empty<ISearchProvider>());

        var found = catalog.TryGet("openai", out var descriptor);

        Assert.True(found);
        Assert.Equal("openai", descriptor?.Name);
        Assert.Equal(ProviderCategory.Llm, descriptor?.Category);
    }

    [Fact]
    public void TryGet_UnknownName_ReturnsFalse() {
        var catalog = new Services.Web.ProviderCatalog.ProviderCatalog(new FakeLlmRegistry(), Array.Empty<ISearchProvider>());

        var found = catalog.TryGet("nonexistent", out var descriptor);

        Assert.False(found);
        Assert.Null(descriptor);
    }

    [Fact]
    public void LlmDescriptor_OpenAiCompatible_SupportsOpenAiFormatTrue() {
        var llmRegistry = new FakeLlmRegistry(
            new FakeLlmDefinition("openai", "OpenAI", ProtocolKind.OpenAiCompatible, "sk-test")
        );
        var catalog = new Services.Web.ProviderCatalog.ProviderCatalog(llmRegistry, Array.Empty<ISearchProvider>());

        catalog.TryGet("openai", out var desc);
        Assert.True(desc?.SupportsOpenAiFormat);
    }

    [Fact]
    public void LlmDescriptor_Anthropic_SupportsOpenAiFormatFalse() {
        var llmRegistry = new FakeLlmRegistry(
            new FakeLlmDefinition("anthropic", "Anthropic", ProtocolKind.Anthropic, "sk-test")
        );
        var catalog = new Services.Web.ProviderCatalog.ProviderCatalog(llmRegistry, Array.Empty<ISearchProvider>());

        catalog.TryGet("anthropic", out var desc);
        Assert.False(desc?.SupportsOpenAiFormat);
    }

    [Fact]
    public void SearchDescriptor_SupportsOpenAiFormatAlwaysFalse() {
        var searchProviders = new ISearchProvider[] { new FakeSearchProvider("Tavily", 100, true) };
        var catalog = new Services.Web.ProviderCatalog.ProviderCatalog(new FakeLlmRegistry(), searchProviders);

        catalog.TryGet("Tavily", out var desc);
        Assert.False(desc?.SupportsOpenAiFormat);
    }

    [Fact]
    public void LlmDescriptor_ApiKeyConfigured_IsAvailableTrue() {
        var llmRegistry = new FakeLlmRegistry(
            new FakeLlmDefinition("openai", "OpenAI", ProtocolKind.OpenAiCompatible, "sk-test")
        );
        var catalog = new Services.Web.ProviderCatalog.ProviderCatalog(llmRegistry, Array.Empty<ISearchProvider>());

        catalog.TryGet("openai", out var desc);
        Assert.True(desc?.IsAvailable);
    }

    [Fact]
    public void LlmDescriptor_ApiKeyMissing_IsAvailableFalse() {
        var llmRegistry = new FakeLlmRegistry(
            new FakeLlmDefinition("openai", "OpenAI", ProtocolKind.OpenAiCompatible, null)
        );
        var catalog = new Services.Web.ProviderCatalog.ProviderCatalog(llmRegistry, Array.Empty<ISearchProvider>());

        catalog.TryGet("openai", out var desc);
        Assert.False(desc?.IsAvailable);
    }

    [Fact]
    public void LlmDescriptor_DisplayName_Returned() {
        var llmRegistry = new FakeLlmRegistry(
            new FakeLlmDefinition("openai", "OpenAI", ProtocolKind.OpenAiCompatible, "sk-test")
        );
        var catalog = new Services.Web.ProviderCatalog.ProviderCatalog(llmRegistry, Array.Empty<ISearchProvider>());

        catalog.TryGet("openai", out var desc);
        Assert.Equal("OpenAI", desc?.DisplayName);
    }
}
