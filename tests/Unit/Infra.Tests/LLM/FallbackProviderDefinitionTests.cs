
namespace Infra.Tests.LLM;

public sealed class FallbackProviderDefinitionTests
{
    [Fact]
    public void GetBaseUrl_OpenAI_ReturnsDefaultEndpoint()
    {
        var def = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig();
        var url = def.GetBaseUrl(config);

        url.Should().Be("https://api.openai.com/v1/");
    }

    [Fact]
    public void GetBaseUrl_OpenAI_WithCustomEndpoint_ReturnsCustomEndpoint()
    {
        var def = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { Endpoint = "https://custom.api.com/v1" };
        var url = def.GetBaseUrl(config);

        url.Should().Be("https://custom.api.com/v1/");
    }

    [Fact]
    public void GetBaseUrl_Anthropic_ReturnsDefaultEndpoint()
    {
        var def = new FallbackProviderDefinition(ProviderKind.Anthropic);
        var config = new ProviderConfig();
        var url = def.GetBaseUrl(config);

        url.Should().Be("https://api.anthropic.com/");
    }

    [Fact]
    public void GetBaseUrl_Azure_ReturnsDeploymentPath()
    {
        var def = new FallbackProviderDefinition(ProviderKind.Azure);
        var config = new ProviderConfig { Endpoint = "https://my-resource.openai.azure.com", ModelId = "gpt-4o" };
        var url = def.GetBaseUrl(config);

        url.Should().Be("https://my-resource.openai.azure.com/openai/deployments/gpt-4o");
    }

    [Fact]
    public void GetChatEndpoint_OpenAI_ReturnsChatCompletions()
    {
        var def = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig();
        var endpoint = def.GetChatEndpoint(config);

        endpoint.Should().Be("chat/completions");
    }

    [Fact]
    public void GetChatEndpoint_Anthropic_ReturnsV1Messages()
    {
        var def = new FallbackProviderDefinition(ProviderKind.Anthropic);
        var config = new ProviderConfig();
        var endpoint = def.GetChatEndpoint(config);

        endpoint.Should().Be("v1/messages");
    }

    [Fact]
    public void GetChatEndpoint_Azure_ReturnsWithApiVersion()
    {
        var def = new FallbackProviderDefinition(ProviderKind.Azure);
        var config = new ProviderConfig { ApiVersion = "2024-02-01" };
        var endpoint = def.GetChatEndpoint(config);

        endpoint.Should().Be("chat/completions?api-version=2024-02-01");
    }

    [Fact]
    public void ConfigureHttpClient_OpenAI_SetsBearerToken()
    {
        var def = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { ApiKey = "sk-test" };
        using var client = new HttpClient();
        def.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.Authorization!.Scheme.Should().Be("Bearer");
        client.DefaultRequestHeaders.Authorization!.Parameter.Should().Be("sk-test");
    }

    [Fact]
    public void ConfigureHttpClient_Anthropic_SetsXApiKeyHeader()
    {
        var def = new FallbackProviderDefinition(ProviderKind.Anthropic);
        var config = new ProviderConfig { ApiKey = "sk-ant-test" };
        using var client = new HttpClient();
        def.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.GetValues("x-api-key").First().Should().Be("sk-ant-test");
        client.DefaultRequestHeaders.GetValues("anthropic-version").First().Should().Be("2024-10-22");
    }

    [Fact]
    public void ConfigureHttpClient_Azure_SetsApiKeyHeader()
    {
        var def = new FallbackProviderDefinition(ProviderKind.Azure);
        var config = new ProviderConfig { ApiKey = "azure-key" };
        using var client = new HttpClient();
        def.ConfigureHttpClient(client, config);

        client.DefaultRequestHeaders.GetValues("api-key").First().Should().Be("azure-key");
    }

    [Fact]
    public void Kind_ReturnsCorrectProviderKind()
    {
        new FallbackProviderDefinition(ProviderKind.OpenAI).Kind.Should().Be(ProviderKind.OpenAI);
        new FallbackProviderDefinition(ProviderKind.Azure).Kind.Should().Be(ProviderKind.Azure);
        new FallbackProviderDefinition(ProviderKind.Anthropic).Kind.Should().Be(ProviderKind.Anthropic);
        new FallbackProviderDefinition(ProviderKind.Agnes).Kind.Should().Be(ProviderKind.Agnes);
    }

    [Fact]
    public void IsValid_WithApiKey_ReturnsTrue()
    {
        var def = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { ApiKey = "sk-test" };
        def.IsValid(config).Should().BeTrue();
    }

    [Fact]
    public void IsValid_WithoutApiKey_ReturnsFalse()
    {
        var def = new FallbackProviderDefinition(ProviderKind.OpenAI);
        var config = new ProviderConfig { ApiKey = "" };
        def.IsValid(config).Should().BeFalse();
    }
}
