namespace Llm.Tests.Adapters.LLM.QueryServices;

using System.Net.Http;
using Api.LLM.QueryServices;

public class FallbackProviderDefinitionTests
{
    #region Kind-only constructor

    [Theory]
    [InlineData(ProviderKind.OpenAI, "openai")]
    [InlineData(ProviderKind.Anthropic, "anthropic")]
    [InlineData(ProviderKind.Azure, "azure")]
    [InlineData(ProviderKind.Agnes, "agnes")]
    public void KindConstructor_ExposesKindAndProviderName(ProviderKind kind, string expectedName)
    {
        var definition = new FallbackProviderDefinition(kind);

        definition.Kind.Should().Be(kind);
        definition.ProviderName.Should().Be(expectedName);
        definition.DisplayName.Should().Be(expectedName);
    }

    [Fact]
    public void KindConstructor_OpenAI_ReturnsDefaultModelIdsFromConfigLoader()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);

        definition.DefaultModelId.Should().NotBeNull();
        definition.DefaultFastModelId.Should().NotBeNull();
    }

    [Fact]
    public void KindConstructor_UnknownProvider_FallsBackToOpenAIDefaultModelIds()
    {
        // 未知 ProviderKind 回退到 OpenAI 默认值（KindToConfigKey 的 _ => "openai" 分支）
        var definition = new FallbackProviderDefinition((ProviderKind)999);

        definition.DefaultModelId.Should().NotBeEmpty();
        definition.DefaultFastModelId.Should().NotBeEmpty();
    }

    [Fact]
    public void KindConstructor_AvailableModels_ReturnsEmptyList()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        definition.AvailableModels.Should().BeEmpty();
    }

    #endregion

    #region Inner-definition constructor

    [Fact]
    public void InnerConstructor_DelegatesPropertiesToInner()
    {
        var inner = new Mock<IProviderDefinition>();
        inner.Setup(d => d.Kind).Returns(ProviderKind.OpenAI);
        inner.Setup(d => d.ProviderName).Returns("custom");
        inner.Setup(d => d.DisplayName).Returns("Custom Provider");
        inner.Setup(d => d.DefaultModelId).Returns("custom-model");
        inner.Setup(d => d.DefaultFastModelId).Returns("custom-fast");
        inner.Setup(d => d.DefaultEndpoint).Returns("https://custom.example.com");
        inner.Setup(d => d.ApiKeyEnvironmentVariable).Returns("CUSTOM_KEY");
        inner.Setup(d => d.EndpointEnvironmentVariable).Returns("CUSTOM_ENDPOINT");
        inner.Setup(d => d.AvailableModels).Returns([new ModelEntry("m", "M", 1, "d")]);
        inner.Setup(d => d.ResolveApiKeyFromEnv()).Returns("env-key");

        var definition = new FallbackProviderDefinition(inner.Object);

        definition.Kind.Should().Be(ProviderKind.OpenAI);
        definition.ProviderName.Should().Be("custom");
        definition.DisplayName.Should().Be("Custom Provider");
        definition.DefaultModelId.Should().Be("custom-model");
        definition.DefaultFastModelId.Should().Be("custom-fast");
        definition.DefaultEndpoint.Should().Be("https://custom.example.com");
        definition.ApiKeyEnvironmentVariable.Should().Be("CUSTOM_KEY");
        definition.EndpointEnvironmentVariable.Should().Be("CUSTOM_ENDPOINT");
        definition.AvailableModels.Should().ContainSingle();
        definition.ResolveApiKeyFromEnv().Should().Be("env-key");
    }

    [Fact]
    public void InnerConstructor_IsValid_DelegatesToInner()
    {
        var config = new ProviderConfig { Provider = "openai", ApiKey = "" };
        var inner = new Mock<IProviderDefinition>();
        inner.Setup(d => d.IsValid(config)).Returns(true);

        var definition = new FallbackProviderDefinition(inner.Object);

        definition.IsValid(config).Should().BeTrue();
    }

    [Fact]
    public void InnerConstructor_GetBaseUrl_DelegatesToInner()
    {
        var config = new ProviderConfig { Provider = "openai" };
        var inner = new Mock<IProviderDefinition>();
        inner.Setup(d => d.GetBaseUrl(config)).Returns("https://inner.example.com/");

        var definition = new FallbackProviderDefinition(inner.Object);

        definition.GetBaseUrl(config).Should().Be("https://inner.example.com/");
    }

    #endregion

    #region GetBaseUrl

    [Fact]
    public void GetBaseUrl_OpenAI_NoEndpoint_ReturnsDefaultOpenAI()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai" };

        definition.GetBaseUrl(config).Should().Be("https://api.openai.com/v1/");
    }

    [Fact]
    public void GetBaseUrl_OpenAI_WithEndpoint_AppendsTrailingSlash()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai", Endpoint = "https://proxy.example.com" };

        definition.GetBaseUrl(config).Should().Be("https://proxy.example.com/");
    }

    [Fact]
    public void GetBaseUrl_Anthropic_NoEndpoint_ReturnsDefaultAnthropic()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Anthropic);
        var config = new ProviderConfig { Provider = "anthropic" };

        definition.GetBaseUrl(config).Should().Be("https://api.anthropic.com/");
    }

    [Fact]
    public void GetBaseUrl_Anthropic_WithEndpoint_AppendsTrailingSlash()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Anthropic);
        var config = new ProviderConfig { Provider = "anthropic", Endpoint = "https://anthropic.proxy.com" };

        definition.GetBaseUrl(config).Should().Be("https://anthropic.proxy.com/");
    }

    [Fact]
    public void GetBaseUrl_Azure_ReturnsDeploymentUrl()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Azure);
        var config = new ProviderConfig { Provider = "azure", Endpoint = "https://azure.openai.azure.com", ModelId = "gpt-4o" };

        definition.GetBaseUrl(config).Should().Be("https://azure.openai.azure.com/openai/deployments/gpt-4o");
    }

    [Fact]
    public void GetBaseUrl_UnknownKind_ReturnsOpenAIDefault()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.DeepSeek);
        var config = new ProviderConfig { Provider = "deepseek" };

        definition.GetBaseUrl(config).Should().Be("https://api.openai.com/v1/");
    }

    #endregion

    #region GetChatEndpoint

    [Fact]
    public void GetChatEndpoint_Anthropic_ReturnsMessagesPath()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Anthropic);
        var config = new ProviderConfig { Provider = "anthropic" };

        definition.GetChatEndpoint(config).Should().Be("v1/messages");
    }

    [Fact]
    public void GetChatEndpoint_Azure_ReturnsCompletionsWithApiVersion()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Azure);
        var config = new ProviderConfig { Provider = "azure", ApiVersion = "2024-06-01" };

        definition.GetChatEndpoint(config).Should().Be("chat/completions?api-version=2024-06-01");
    }

    [Fact]
    public void GetChatEndpoint_OpenAI_SimpleEndpoint_ReturnsCompletions()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai" };

        definition.GetChatEndpoint(config).Should().Be("chat/completions");
    }

    [Fact]
    public void GetChatEndpoint_OpenAI_EndpointAlreadyEndsWithCompletions_ReturnsEmpty()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai", Endpoint = "https://proxy.example.com/chat/completions" };

        definition.GetChatEndpoint(config).Should().BeEmpty();
    }

    #endregion

    #region ConfigureHttpClient

    [Fact]
    public void ConfigureHttpClient_OpenAI_AddsBearerAuthorization()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var client = new HttpClient();
        var config = new ProviderConfig { Provider = "openai", ApiKey = "sk-test" };

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Authorization.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        client.DefaultRequestHeaders.Authorization.Parameter.Should().Be("sk-test");
    }

    [Fact]
    public void ConfigureHttpClient_OpenAI_EmptyApiKey_DoesNotAddHeader()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var client = new HttpClient();
        var config = new ProviderConfig { Provider = "openai", ApiKey = "" };

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Fact]
    public void ConfigureHttpClient_Anthropic_AddsApiKeyAndVersionHeaders()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Anthropic);
        var client = new HttpClient();
        var config = new ProviderConfig { Provider = "anthropic", ApiKey = "ak-test" };

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Contains("x-api-key").Should().BeTrue();
        client.DefaultRequestHeaders.GetValues("x-api-key").Single().Should().Be("ak-test");
        client.DefaultRequestHeaders.GetValues("anthropic-version").Single().Should().Be("2024-10-22");
    }

    [Fact]
    public void ConfigureHttpClient_Azure_AddsApiKeyHeader()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Azure);
        var client = new HttpClient();
        var config = new ProviderConfig { Provider = "azure", ApiKey = "az-test" };

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.GetValues("api-key").Single().Should().Be("az-test");
    }

    [Fact]
    public void ConfigureHttpClient_WithInner_DelegatesToInner()
    {
        var inner = new Mock<IProviderDefinition>();
        var definition = new FallbackProviderDefinition(inner.Object);
        var client = new HttpClient();
        var config = new ProviderConfig { Provider = "openai" };

        definition.ConfigureHttpClient(client, config);

        inner.Verify(d => d.ConfigureHttpClient(client, config), Times.Once);
    }

    #endregion

    #region IsValid

    [Fact]
    public void IsValid_WithoutInner_NonWhiteSpaceApiKey_ReturnsTrue()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai", ApiKey = "sk-test" };

        definition.IsValid(config).Should().BeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsValid_WithoutInner_InvalidApiKey_ReturnsFalse(string? apiKey)
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai", ApiKey = apiKey! };

        definition.IsValid(config).Should().BeFalse();
    }

    #endregion
}
