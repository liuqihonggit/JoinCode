using Api.LLM.QueryServices;

namespace Llm.Tests.Adapters.QueryServices;

public sealed class FallbackProviderDefinitionTests
{
    #region OpenAI

    [Fact]
    public void OpenAI_GetBaseUrl_WithoutEndpoint_ReturnsDefault()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai" };

        definition.GetBaseUrl(config).Should().Be("https://api.openai.com/v1/");
    }

    [Fact]
    public void OpenAI_GetBaseUrl_WithEndpoint_ReturnsNormalizedEndpoint()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai", Endpoint = "https://proxy.example.com/api" };

        definition.GetBaseUrl(config).Should().Be("https://proxy.example.com/api/");
    }

    [Fact]
    public void OpenAI_GetBaseUrl_WithEndpointTrailingSlash_ReturnsNormalizedEndpoint()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai", Endpoint = "https://proxy.example.com/api/" };

        definition.GetBaseUrl(config).Should().Be("https://proxy.example.com/api/");
    }

    [Fact]
    public void OpenAI_GetChatEndpoint_WithoutChatCompletionsSuffix_ReturnsDefault()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai" };

        definition.GetChatEndpoint(config).Should().Be("chat/completions");
    }

    [Fact]
    public void OpenAI_GetChatEndpoint_WithChatCompletionsSuffix_ReturnsEmpty()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai", Endpoint = "https://proxy.example.com/chat/completions" };

        definition.GetChatEndpoint(config).Should().BeEmpty();
    }

    [Fact]
    public void OpenAI_ConfigureHttpClient_AddsBearerAuthorization()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai", ApiKey = "sk-test" };
        using var client = new HttpClient();

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Authorization.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        client.DefaultRequestHeaders.Authorization.Parameter.Should().Be("sk-test");
    }

    [Fact]
    public void OpenAI_ConfigureHttpClient_WithoutApiKey_DoesNotAddAuth()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Provider = "openai", ApiKey = "" };
        using var client = new HttpClient();

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    #endregion

    #region Anthropic

    [Fact]
    public void Anthropic_GetBaseUrl_WithoutEndpoint_ReturnsDefault()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Anthropic);
        var config = new ProviderConfig { Provider = "anthropic" };

        definition.GetBaseUrl(config).Should().Be("https://api.anthropic.com/");
    }

    [Fact]
    public void Anthropic_GetChatEndpoint_ReturnsMessages()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Anthropic);
        var config = new ProviderConfig { Provider = "anthropic" };

        definition.GetChatEndpoint(config).Should().Be("v1/messages");
    }

    [Fact]
    public void Anthropic_ConfigureHttpClient_AddsApiKeyAndVersion()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Anthropic);
        var config = new ProviderConfig { Provider = "anthropic", ApiKey = "sk-ant" };
        using var client = new HttpClient();

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Contains("x-api-key").Should().BeTrue();
        client.DefaultRequestHeaders.Contains("anthropic-version").Should().BeTrue();
        client.DefaultRequestHeaders.GetValues("x-api-key").Should().ContainSingle("sk-ant");
        client.DefaultRequestHeaders.GetValues("anthropic-version").Should().ContainSingle("2024-10-22");
    }

    #endregion

    #region Azure

    [Fact]
    public void Azure_GetBaseUrl_IncludesDeploymentModel()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Azure);
        var config = new ProviderConfig { Provider = "azure", Endpoint = "https://test.openai.azure.com", ModelId = "gpt-4o" };

        definition.GetBaseUrl(config).Should().Be("https://test.openai.azure.com/openai/deployments/gpt-4o");
    }

    [Fact]
    public void Azure_GetChatEndpoint_IncludesApiVersion()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Azure);
        var config = new ProviderConfig { Provider = "azure", ApiVersion = "2024-06-01" };

        definition.GetChatEndpoint(config).Should().Be("chat/completions?api-version=2024-06-01");
    }

    [Fact]
    public void Azure_ConfigureHttpClient_AddsApiKeyHeader()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Azure);
        var config = new ProviderConfig { Provider = "azure", ApiKey = "key123" };
        using var client = new HttpClient();

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Contains("api-key").Should().BeTrue();
        client.DefaultRequestHeaders.GetValues("api-key").Should().ContainSingle("key123");
    }

    #endregion

    #region DeepSeek

    [Fact]
    public void DeepSeek_GetBaseUrl_WithoutEndpoint_ReturnsOpenAiDefault()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.DeepSeek);
        var config = new ProviderConfig { Provider = "deepseek" };

        definition.GetBaseUrl(config).Should().Be("https://api.openai.com/v1/");
    }

    [Fact]
    public void DeepSeek_GetChatEndpoint_WithoutChatCompletionsSuffix_ReturnsDefault()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.DeepSeek);
        var config = new ProviderConfig { Provider = "deepseek" };

        definition.GetChatEndpoint(config).Should().Be("chat/completions");
    }

    [Fact]
    public void DeepSeek_ConfigureHttpClient_AddsBearerAuthorization()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.DeepSeek);
        var config = new ProviderConfig { Provider = "deepseek", ApiKey = "sk-ds" };
        using var client = new HttpClient();

        definition.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Authorization.Should().NotBeNull();
        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
    }

    #endregion

    #region Agnes

    [Fact]
    public void Agnes_GetBaseUrl_WithoutEndpoint_ReturnsOpenAiDefault()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.Agnes);
        var config = new ProviderConfig { Provider = "agnes" };

        definition.GetBaseUrl(config).Should().Be("https://api.openai.com/v1/");
    }

    #endregion

    #region Wrapping inner definition

    [Fact]
    public void WrapInner_DelegatesProperties()
    {
        var inner = new Mock<IProviderDefinition>();
        inner.Setup(d => d.Kind).Returns(ProviderKind.Anthropic);
        inner.Setup(d => d.ProviderName).Returns("anthropic");
        inner.Setup(d => d.DisplayName).Returns("Anthropic");
        inner.Setup(d => d.DefaultModelId).Returns("claude");
        inner.Setup(d => d.DefaultFastModelId).Returns("claude-fast");
        inner.Setup(d => d.AvailableModels).Returns([new ModelEntry("claude", "Claude", 200000)]);
        inner.Setup(d => d.IsValid(It.IsAny<ProviderConfig>())).Returns(true);

        var definition = new FallbackProviderDefinition(inner.Object);

        definition.Kind.Should().Be(ProviderKind.Anthropic);
        definition.ProviderName.Should().Be("anthropic");
        definition.DisplayName.Should().Be("Anthropic");
        definition.DefaultModelId.Should().Be("claude");
        definition.DefaultFastModelId.Should().Be("claude-fast");
        definition.AvailableModels.Should().HaveCount(1);
        definition.IsValid(new ProviderConfig()).Should().BeTrue();
    }

    [Fact]
    public void WrapInner_DelegatesBaseUrlAndEndpoint()
    {
        var inner = new Mock<IProviderDefinition>();
        inner.Setup(d => d.GetBaseUrl(It.IsAny<ProviderConfig>())).Returns("https://inner.example/");
        inner.Setup(d => d.GetChatEndpoint(It.IsAny<ProviderConfig>())).Returns("inner/endpoint");

        var definition = new FallbackProviderDefinition(inner.Object);
        var config = new ProviderConfig { Provider = "anthropic" };

        definition.GetBaseUrl(config).Should().Be("https://inner.example/");
        definition.GetChatEndpoint(config).Should().Be("inner/endpoint");
    }

    [Fact]
    public void WrapInner_DelegatesConfigureHttpClient()
    {
        var inner = new Mock<IProviderDefinition>();
        using var client = new HttpClient();
        var config = new ProviderConfig { Provider = "anthropic" };

        var definition = new FallbackProviderDefinition(inner.Object);
        definition.ConfigureHttpClient(client, config);

        inner.Verify(d => d.ConfigureHttpClient(client, config), Times.Once);
    }

    #endregion

    #region IsValid fallback

    [Fact]
    public void IsValid_Fallback_RequiresApiKey()
    {
        var definition = new FallbackProviderDefinition(ProviderKind.OpenAI);

        definition.IsValid(new ProviderConfig { ApiKey = "key" }).Should().BeTrue();
        definition.IsValid(new ProviderConfig { ApiKey = "" }).Should().BeFalse();
        definition.IsValid(new ProviderConfig { ApiKey = "   " }).Should().BeFalse();
    }

    #endregion
}
