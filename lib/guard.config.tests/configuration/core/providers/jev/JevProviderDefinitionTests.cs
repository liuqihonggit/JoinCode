namespace Guard.Tests.Configuration;

/// <summary>
/// JevProviderDefinition 单元测试 — 端点构建/认证头/模型能力查询
/// </summary>
public class JevProviderDefinitionTests {
    private static ModelConfigLoader CreateLoader() {
        var loader = new ModelConfigLoader();
        loader.ApplyProviders(new Dictionary<string, ModelProviderConfig>(StringComparer.OrdinalIgnoreCase) {
            ["jev"] = new ModelProviderConfig {
                DefaultModelId = "jev-latest",
                Models = [new ModelItemConfig { Id = "jev-latest", DisplayName = "Jev Latest", ContextWindow = 10000 }]
            }
        });
        return loader;
    }

    private static JevProviderDefinition CreateDefinition() => new(CreateLoader());

    #region 基本属性

    [Fact]
    public void Vendor_ShouldBeJev() {
        CreateDefinition().Vendor.Should().Be(VendorKind.Jev);
    }

    [Fact]
    public void Protocol_ShouldBeJev() {
        CreateDefinition().Protocol.Should().Be(ProtocolKind.Jev);
    }

    [Fact]
    public void ProviderName_ShouldBeJev() {
        CreateDefinition().ProviderName.Should().Be("jev");
    }

    [Fact]
    public void DisplayName_ShouldBeJevTypeSafeAi() {
        CreateDefinition().DisplayName.Should().Be("Jev (TypeSafe AI)");
    }

    [Fact]
    public void DefaultEndpoint_ShouldBeTypeSafeSystemOne() {
        CreateDefinition().DefaultEndpoint.Should().Be("https://api.typesafe.ai/v1/systemone");
    }

    [Fact]
    public void ApiKeyEnvironmentVariable_ShouldBeJevApiKey() {
        CreateDefinition().ApiKeyEnvironmentVariable.Should().Be("JEV_API_KEY");
    }

    [Fact]
    public void DefaultModelId_ShouldComeFromLoader() {
        CreateDefinition().DefaultModelId.Should().Be("jev-latest");
    }

    #endregion

    #region GetBaseUrl

    [Fact]
    public void GetBaseUrl_WithConfiguredEndpoint_ShouldUseConfigured() {
        var definition = CreateDefinition();
        var config = new ProviderConfig { Endpoint = "https://custom.typesafe.ai/v1/" };

        definition.GetBaseUrl(config).Should().Be("https://custom.typesafe.ai/v1/");
    }

    [Fact]
    public void GetBaseUrl_WithoutEndpoint_ShouldFallbackToOfficial() {
        var definition = CreateDefinition();
        var config = new ProviderConfig();

        definition.GetBaseUrl(config).Should().Be("https://api.typesafe.ai/v1/");
    }

    [Fact]
    public void GetBaseUrl_WithTrailingSlash_ShouldNotDoubleSlash() {
        var definition = CreateDefinition();
        var config = new ProviderConfig { Endpoint = "https://custom.typesafe.ai/v1//" };

        definition.GetBaseUrl(config).Should().Be("https://custom.typesafe.ai/v1/");
    }

    #endregion

    #region GetChatEndpoint

    [Fact]
    public void GetChatEndpoint_ShouldBeSystemOne() {
        var definition = CreateDefinition();
        var config = new ProviderConfig();

        definition.GetChatEndpoint(config).Should().Be("systemone");
    }

    #endregion

    #region ConfigureHttpClient

    [Fact]
    public void ConfigureHttpClient_WithApiKey_ShouldAddBearerAuth() {
        var definition = CreateDefinition();
        var client = new HttpClient();
        var config = new ProviderConfig { ApiKey = "jev-sk-test-123" };

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        client.DefaultRequestHeaders.Authorization!.Parameter.Should().Be("jev-sk-test-123");
    }

    [Fact]
    public void ConfigureHttpClient_WithoutApiKey_ShouldNotAddAuthHeader() {
        var definition = CreateDefinition();
        var client = new HttpClient();
        var config = new ProviderConfig();

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    #endregion

    #region IsValid

    [Fact]
    public void IsValid_WithApiKey_ShouldBeTrue() {
        var definition = CreateDefinition();
        var config = new ProviderConfig { ApiKey = "jev-sk-test" };

        definition.IsValid(config).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithoutApiKey_ShouldBeFalse() {
        var definition = CreateDefinition();
        var config = new ProviderConfig();

        definition.IsValid(config).Should().BeFalse();
    }

    #endregion

    #region ResolveApiKeyFromEnv

    [Fact]
    public void ResolveApiKeyFromEnv_WhenEnvVarSet_ShouldReturnValue() {
        Environment.SetEnvironmentVariable("JEV_API_KEY", "env-jev-key");
        try {
            CreateDefinition().ResolveApiKeyFromEnv().Should().Be("env-jev-key");
        } finally {
            Environment.SetEnvironmentVariable("JEV_API_KEY", null);
        }
    }

    [Fact]
    public void ResolveApiKeyFromEnv_WhenEnvVarNotSet_ShouldReturnNull() {
        Environment.SetEnvironmentVariable("JEV_API_KEY", null);
        CreateDefinition().ResolveApiKeyFromEnv().Should().BeNull();
    }

    #endregion
}
