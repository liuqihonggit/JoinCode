namespace Guard.Config.Tests.Configuration.ConfigRoot;

public sealed class ConfigLayerTests {
    private static SettingsJson NewSettingsWithSearchProviders() {
        return new SettingsJson {
            SearchProviders = new Dictionary<string, SearchProviderProfileSettings> {
                ["tavily"] = new() { ApiKeyEnvVar = "TAVILY_API_KEY", Priority = 100, Enabled = true },
                ["searxng"] = new() { EndpointEnvVar = "SEARXNG_URL", Priority = 400, Enabled = false },
            }
        };
    }

    [Fact]
    public void Exists_SettingsNotNull_ReturnsTrue() {
        var layer = new ConfigLayer(NewSettingsWithSearchProviders());
        Assert.True(layer.Exists);
    }

    [Fact]
    public void Exists_SettingsNull_ReturnsFalse() {
        var layer = new ConfigLayer(null);
        Assert.False(layer.Exists);
    }

    [Fact]
    public void TryGet_SearchProvidersApiKeyEnvVar_ReturnsTrueAndValue() {
        var layer = new ConfigLayer(NewSettingsWithSearchProviders());
        var found = layer.TryGet<string>("searchProviders.tavily.apiKeyEnvVar", out var result);
        Assert.True(found);
        Assert.Equal("TAVILY_API_KEY", result);
    }

    [Fact]
    public void TryGet_SearchProvidersEndpointEnvVar_ReturnsTrueAndValue() {
        var layer = new ConfigLayer(NewSettingsWithSearchProviders());
        var found = layer.TryGet<string>("searchProviders.searxng.endpointEnvVar", out var result);
        Assert.True(found);
        Assert.Equal("SEARXNG_URL", result);
    }

    [Fact]
    public void TryGet_SearchProvidersPriority_ReturnsTrueAndString() {
        var layer = new ConfigLayer(NewSettingsWithSearchProviders());
        var found = layer.TryGet<string>("searchProviders.tavily.priority", out var result);
        Assert.True(found);
        Assert.Equal("100", result);
    }

    [Fact]
    public void TryGet_SearchProvidersEnabled_ReturnsTrueAndString() {
        var layer = new ConfigLayer(NewSettingsWithSearchProviders());
        var found = layer.TryGet<string>("searchProviders.tavily.enabled", out var result);
        Assert.True(found);
        Assert.Equal("True", result);
    }

    [Fact]
    public void TryGet_UnknownProvider_ReturnsFalse() {
        var layer = new ConfigLayer(NewSettingsWithSearchProviders());
        var found = layer.TryGet<string>("searchProviders.unknown.apiKeyEnvVar", out var result);
        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void TryGet_UnknownField_ReturnsFalse() {
        var layer = new ConfigLayer(NewSettingsWithSearchProviders());
        var found = layer.TryGet<string>("searchProviders.tavily.unknownField", out var result);
        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void TryGet_UnknownTopLevelNode_ReturnsFalse() {
        var layer = new ConfigLayer(NewSettingsWithSearchProviders());
        var found = layer.TryGet<string>("unknownNode.tavily.field", out var result);
        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void TryGet_SettingsNull_ReturnsFalse() {
        var layer = new ConfigLayer(null);
        var found = layer.TryGet<string>("searchProviders.tavily.apiKeyEnvVar", out var result);
        Assert.False(found);
        Assert.Null(result);
    }

    [Fact]
    public void TryGet_TooFewSegments_ReturnsFalse() {
        var layer = new ConfigLayer(NewSettingsWithSearchProviders());
        var found = layer.TryGet<string>("searchProviders.tavily", out var result);
        Assert.False(found);
        Assert.Null(result);
    }
}
