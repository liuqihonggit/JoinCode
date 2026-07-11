namespace JoinCode.Entry.Tests;

public sealed class ProviderDefinitionPolymorphismTests
{
    [Fact]
    public void OpenAI_RequiresInteractiveEndpoint_ShouldBeFalse()
    {
        var def = new Core.Configuration.Providers.OpenAIProviderDefinition();
        def.RequiresInteractiveEndpoint.Should().BeFalse();
    }

    [Fact]
    public void Azure_RequiresInteractiveEndpoint_ShouldBeTrue()
    {
        var def = new Core.Configuration.Providers.AzureProviderDefinition();
        def.RequiresInteractiveEndpoint.Should().BeTrue();
    }

    [Fact]
    public void Anthropic_RequiresInteractiveEndpoint_ShouldBeFalse()
    {
        IProviderDefinition def = new Core.Configuration.Providers.AnthropicProviderDefinition();
        def.RequiresInteractiveEndpoint.Should().BeFalse();
    }

    [Fact]
    public void Agnes_RequiresInteractiveEndpoint_ShouldBeFalse()
    {
        IProviderDefinition def = new Core.Configuration.Providers.AgnesProviderDefinition();
        def.RequiresInteractiveEndpoint.Should().BeFalse();
    }

    [Fact]
    public void OpenAI_IsCompoundAuthFormat_ShouldReturnFalse()
    {
        IProviderDefinition def = new Core.Configuration.Providers.OpenAIProviderDefinition();
        def.IsCompoundAuthFormat("any-key").Should().BeFalse();
    }

    [Fact]
    public void Azure_IsCompoundAuthFormat_ShouldDetectJsonObject()
    {
        var def = new Core.Configuration.Providers.AzureProviderDefinition();
        def.IsCompoundAuthFormat("{\"apiKey\":\"k\",\"endpoint\":\"e\"}").Should().BeTrue();
        def.IsCompoundAuthFormat("plain-key").Should().BeFalse();
    }

    [Fact]
    public void OpenAI_SerializeAuthCredentials_ShouldReturnApiKeyDirectly()
    {
        IProviderDefinition def = new Core.Configuration.Providers.OpenAIProviderDefinition();
        var result = def.SerializeAuthCredentials("my-key", null);
        result.Should().Be("my-key");
    }

    [Fact]
    public void Azure_SerializeAuthCredentials_ShouldReturnCompoundJson()
    {
        var def = new Core.Configuration.Providers.AzureProviderDefinition();
        var result = def.SerializeAuthCredentials("my-key", "https://test.openai.azure.com");
        result.Should().Contain("\"apiKey\"");
        result.Should().Contain("\"endpoint\"");
        result.Should().Contain("my-key");
        result.Should().Contain("https://test.openai.azure.com");
    }

    [Fact]
    public void Azure_ExtractApiKeyFromCompound_ShouldExtractKey()
    {
        var def = new Core.Configuration.Providers.AzureProviderDefinition();
        var compound = def.SerializeAuthCredentials("my-key", "https://test.openai.azure.com");
        var extracted = def.ExtractApiKeyFromCompound(compound);
        extracted.Should().Be("my-key");
    }

    [Fact]
    public void OpenAI_ExtractApiKeyFromCompound_ShouldReturnNull()
    {
        IProviderDefinition def = new Core.Configuration.Providers.OpenAIProviderDefinition();
        def.ExtractApiKeyFromCompound("any-key").Should().BeNull();
    }

    [Fact]
    public void Azure_EndpointPromptText_ShouldNotBeNull()
    {
        var def = new Core.Configuration.Providers.AzureProviderDefinition();
        def.EndpointPromptText.Should().NotBeNullOrEmpty();
        def.EndpointRequiredMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void OpenAI_EndpointPromptText_ShouldBeNull()
    {
        IProviderDefinition def = new Core.Configuration.Providers.OpenAIProviderDefinition();
        def.EndpointPromptText.Should().BeNull();
        def.EndpointRequiredMessage.Should().BeNull();
    }

    [Fact]
    public void Anthropic_SupportsWebSearch_ShouldBeTrue()
    {
        IProviderDefinition def = new Core.Configuration.Providers.AnthropicProviderDefinition();
        def.SupportsWebSearch.Should().BeTrue();
    }

    [Fact]
    public void OpenAI_SupportsWebSearch_ShouldBeFalse()
    {
        IProviderDefinition def = new Core.Configuration.Providers.OpenAIProviderDefinition();
        def.SupportsWebSearch.Should().BeFalse();
    }

    [Fact]
    public void Azure_SupportsOAuth_ShouldBeTrue()
    {
        IProviderDefinition def = new Core.Configuration.Providers.AzureProviderDefinition();
        def.SupportsOAuth.Should().BeTrue();
    }

    [Fact]
    public void OpenAI_SupportsOAuth_ShouldBeFalse()
    {
        IProviderDefinition def = new Core.Configuration.Providers.OpenAIProviderDefinition();
        def.SupportsOAuth.Should().BeFalse();
    }

    [Fact]
    public void OpenAI_GetBaseUrl_WithCustomEndpoint_ShouldUseCustom()
    {
        var def = new Core.Configuration.Providers.OpenAIProviderDefinition();
        var config = new ProviderConfig { Endpoint = "https://custom.api.com/v1" };
        def.GetBaseUrl(config).Should().Be("https://custom.api.com/v1/");
    }

    [Fact]
    public void OpenAI_GetBaseUrl_WithoutEndpoint_ShouldUseDefault()
    {
        var def = new Core.Configuration.Providers.OpenAIProviderDefinition();
        var config = new ProviderConfig();
        def.GetBaseUrl(config).Should().Be("https://api.openai.com/v1/");
    }

    [Fact]
    public void Anthropic_GetBaseUrl_WithoutEndpoint_ShouldUseDefault()
    {
        var def = new Core.Configuration.Providers.AnthropicProviderDefinition();
        var config = new ProviderConfig();
        def.GetBaseUrl(config).Should().Be("https://api.anthropic.com/");
    }

    [Fact]
    public void Azure_GetBaseUrl_ShouldIncludeDeploymentPath()
    {
        var def = new Core.Configuration.Providers.AzureProviderDefinition();
        var config = new ProviderConfig
        {
            Endpoint = "https://my-resource.openai.azure.com",
            ModelId = "gpt-4o"
        };
        def.GetBaseUrl(config).Should().Be("https://my-resource.openai.azure.com/openai/deployments/gpt-4o");
    }

    [Fact]
    public void Azure_GetChatEndpoint_ShouldIncludeApiVersion()
    {
        var def = new Core.Configuration.Providers.AzureProviderDefinition();
        var config = new ProviderConfig { ApiVersion = "2024-02-01" };
        def.GetChatEndpoint(config).Should().Be("chat/completions?api-version=2024-02-01");
    }

    [Fact]
    public void Azure_ConfigureHttpClient_ShouldSetApiKeyHeader()
    {
        var def = new Core.Configuration.Providers.AzureProviderDefinition();
        var config = new ProviderConfig { ApiKey = "azure-test-key" };
        using var client = new HttpClient();
        def.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.GetValues("api-key").First().Should().Be("azure-test-key");
        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Fact]
    public void Azure_ConfigureHttpClient_WithoutApiKey_ShouldNotSetHeader()
    {
        var def = new Core.Configuration.Providers.AzureProviderDefinition();
        var config = new ProviderConfig { ApiKey = "" };
        using var client = new HttpClient();
        def.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Contains("api-key").Should().BeFalse();
        client.DefaultRequestHeaders.Authorization.Should().BeNull();
    }

    [Fact]
    public void Agnes_GetBaseUrl_WithoutEndpoint_ShouldUseDefault()
    {
        var def = new Core.Configuration.Providers.AgnesProviderDefinition();
        var config = new ProviderConfig();
        def.GetBaseUrl(config).Should().Be("https://apihub.agnes-ai.com/v1/");
    }

    [Fact]
    public void Registry_AllProviders_ShouldBeRegistered()
    {
        var providers = Core.Configuration.Providers.ProviderDefinitionRegistry.RegisteredProviders;
        providers.Should().Contain("openai");
        providers.Should().Contain("azure");
        providers.Should().Contain("anthropic");
        providers.Should().Contain("agnes");
    }

    [Fact]
    public void Registry_TryGet_ShouldReturnCorrectDefinition()
    {
        var openai = Core.Configuration.Providers.ProviderDefinitionRegistry.TryGet("openai");
        openai.Should().NotBeNull();
        openai!.Kind.Should().Be(ProviderKind.OpenAI);

        var azure = Core.Configuration.Providers.ProviderDefinitionRegistry.TryGet("azure");
        azure.Should().NotBeNull();
        azure!.Kind.Should().Be(ProviderKind.Azure);

        var anthropic = Core.Configuration.Providers.ProviderDefinitionRegistry.TryGet("anthropic");
        anthropic.Should().NotBeNull();
        anthropic!.Kind.Should().Be(ProviderKind.Anthropic);

        var agnes = Core.Configuration.Providers.ProviderDefinitionRegistry.TryGet("agnes");
        agnes.Should().NotBeNull();
        agnes!.Kind.Should().Be(ProviderKind.Agnes);
    }

    [Fact]
    public void Registry_TryGet_UnknownProvider_ShouldReturnNull()
    {
        var result = Core.Configuration.Providers.ProviderDefinitionRegistry.TryGet("unknown");
        result.Should().BeNull();
    }

    [Fact]
    public void OpenAI_ResolveAlias_ShouldMapShortNames()
    {
        var def = new Core.Configuration.Providers.OpenAIProviderDefinition();
        def.ResolveAlias("4o").Should().Be("gpt-4o");
        def.ResolveAlias("4o-mini").Should().Be("gpt-4o-mini");
        def.ResolveAlias("4.1").Should().Be("gpt-4.1");
        def.ResolveAlias("o3").Should().Be("o3");
        def.ResolveAlias("unknown").Should().BeNull();
    }

    [Fact]
    public void Azure_ResolveAlias_ShouldMatchOpenAI()
    {
        var openai = new Core.Configuration.Providers.OpenAIProviderDefinition();
        var azure = new Core.Configuration.Providers.AzureProviderDefinition();
        azure.ResolveAlias("4o").Should().Be(openai.ResolveAlias("4o"));
        azure.ResolveAlias("4o-mini").Should().Be(openai.ResolveAlias("4o-mini"));
        azure.ResolveAlias("o3").Should().Be(openai.ResolveAlias("o3"));
    }

    [Fact]
    public void Anthropic_ResolveAlias_ShouldMapShortNames()
    {
        var def = new Core.Configuration.Providers.AnthropicProviderDefinition();
        def.ResolveAlias("sonnet").Should().Be("claude-sonnet-4-6");
        def.ResolveAlias("opus").Should().Be("claude-opus-4-6");
        def.ResolveAlias("haiku").Should().Be("claude-haiku-4-5-20251001");
        def.ResolveAlias("best").Should().Be("claude-opus-4-6");
        def.ResolveAlias("unknown").Should().BeNull();
    }

    [Fact]
    public void Agnes_ResolveAlias_ShouldMapShortNames()
    {
        var def = new Core.Configuration.Providers.AgnesProviderDefinition();
        def.ResolveAlias("flash").Should().Be("agnes-1.5-flash");
        def.ResolveAlias("flash2").Should().Be("agnes-2.0-flash");
        def.ResolveAlias("unknown").Should().BeNull();
    }
}
